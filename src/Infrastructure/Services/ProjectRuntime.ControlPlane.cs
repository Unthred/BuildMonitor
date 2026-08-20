using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.ControlPlane;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Infrastructure.Services;

internal sealed partial class ProjectRuntime
{
    private ControlPlaneSessionStore? sessionStore;
    private ControlPlaneMetricsStore? metricsStore;
    private bool watchPausedByControlPlane;
    private string? shipCheckConfiguration;
    private string? shipCheckFilter;
    private int shipCheckInProgress;
    private int agentRebuildInProgress;
    private int agentTestsInProgress;
    private ControlPlaneShipCheckPhase shipCheckPhase = ControlPlaneShipCheckPhase.None;
    private ControlPlaneShipCheckPhase agentRebuildPhase = ControlPlaneShipCheckPhase.None;
    private ControlPlaneShipCheckOutcome lastShipCheckOutcome = ControlPlaneShipCheckOutcome.None;
    private ControlPlaneShipCheckOutcome lastAgentRebuildOutcome = ControlPlaneShipCheckOutcome.None;
    private ControlPlaneShipCheckOutcome lastAgentTestsOutcome = ControlPlaneShipCheckOutcome.None;
    private DateTimeOffset? lastShipCheckCompletedUtc;
    private DateTimeOffset? lastAgentRebuildCompletedUtc;
    private DateTimeOffset? lastAgentTestsCompletedUtc;
    private ControlPlaneSessionState? lastPublishedSessionState;

    public void NotifyControlPlaneChanged(bool immediate = true)
    {
        MarkHealthDirty();
        HealthCoalesceRequested?.Invoke(immediate);
    }

    public ProjectControlPlaneSnapshot BuildControlPlaneSnapshot(DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        var sessionStatus = sessionStore?.GetStatus(definition.Id, now);
        var sessionApiUsed = sessionStatus?.SessionApiUsed == true;
        var effectiveState = sessionStatus?.State ?? ControlPlaneSessionState.Idle;
        var autoBuildEnabled = !BuildTriggerPolicy.IsAutoBuildDisabledByMode(definition.BuildControlMode);
        var autoBuildBlocked = !BuildTriggerPolicy.ShouldAutoBuildFromFileChange(
            definition.BuildControlMode,
            sessionStatus?.SessionApiUsed == true,
            effectiveState);
        var inShipCheck = Volatile.Read(ref shipCheckInProgress) != 0;
        var inRebuild = Volatile.Read(ref agentRebuildInProgress) != 0;

        return new ProjectControlPlaneSnapshot(
            SessionApiUsed: sessionApiUsed,
            EffectiveSessionState: effectiveState,
            SessionSinceUtc: sessionStatus?.Since,
            AutoBuildBlockedBySession: autoBuildBlocked && autoBuildEnabled,
            HasPendingFileChangeRebuild: pendingFileChangeRebuild,
            PendingFileChangeCount: pendingRebuildHoldFileCount,
            ShipCheckPhase: inShipCheck
                ? shipCheckPhase
                : ControlPlaneShipCheckPhase.None,
            LastShipCheckOutcome: lastShipCheckOutcome,
            LastShipCheckCompletedUtc: lastShipCheckCompletedUtc,
            ShipCheckInProgress: inShipCheck,
            AgentRebuildInProgress: inRebuild,
            AgentRebuildPhase: inRebuild
                ? agentRebuildPhase
                : ControlPlaneShipCheckPhase.None,
            LastAgentRebuildOutcome: lastAgentRebuildOutcome,
            LastAgentRebuildCompletedUtc: lastAgentRebuildCompletedUtc,
            IdleCause: sessionStatus?.IdleCause ?? ControlPlaneIdleCause.None,
            AgentTestsInProgress: Volatile.Read(ref agentTestsInProgress) != 0,
            LastAgentTestsOutcome: lastAgentTestsOutcome,
            LastAgentTestsCompletedUtc: lastAgentTestsCompletedUtc,
            BuildControlMode: definition.BuildControlMode,
            AutoBuildEnabled: autoBuildEnabled);
    }

    internal void RefreshControlPlaneHealthIfNeeded()
    {
        var snapshot = BuildControlPlaneSnapshot();
        if (!ControlPlaneStatusFormatter.ShouldShowControlPlaneSection(snapshot))
        {
            lastPublishedSessionState = null;
            return;
        }

        var stateChanged = lastPublishedSessionState != snapshot.EffectiveSessionState;
        lastPublishedSessionState = snapshot.EffectiveSessionState;

        if (stateChanged
            || snapshot.EffectiveSessionState == ControlPlaneSessionState.Busy
            || snapshot.ShipCheckInProgress
            || snapshot.AgentRebuildInProgress
            || snapshot.AgentTestsInProgress
            || snapshot.ShipCheckPhase != ControlPlaneShipCheckPhase.None
            || snapshot.AgentRebuildPhase != ControlPlaneShipCheckPhase.None)
        {
            MarkHealthDirty();
        }
    }

    private void SetShipCheckPhase(ControlPlaneShipCheckPhase phase, bool immediate = true)
    {
        shipCheckPhase = phase;
        NotifyControlPlaneChanged(immediate);
    }

    private void CompleteShipCheck(bool ok)
    {
        lastShipCheckOutcome = ok
            ? ControlPlaneShipCheckOutcome.Passed
            : ControlPlaneShipCheckOutcome.Failed;
        lastShipCheckCompletedUtc = DateTimeOffset.UtcNow;
        shipCheckPhase = ControlPlaneShipCheckPhase.None;
        NotifyControlPlaneChanged(immediate: true);
    }

    private void SetAgentRebuildPhase(ControlPlaneShipCheckPhase phase, bool immediate = true)
    {
        agentRebuildPhase = phase;
        NotifyControlPlaneChanged(immediate);
    }

    private void CompleteAgentRebuild(bool ok)
    {
        lastAgentRebuildOutcome = ok
            ? ControlPlaneShipCheckOutcome.Passed
            : ControlPlaneShipCheckOutcome.Failed;
        lastAgentRebuildCompletedUtc = DateTimeOffset.UtcNow;
        agentRebuildPhase = ControlPlaneShipCheckPhase.None;
        NotifyControlPlaneChanged(immediate: true);
    }

    private static void EnsureNoOtherControlPlaneRun(int shipCheckInProgress, int rebuildInProgress, int testsInProgress)
    {
        if (shipCheckInProgress != 0)
        {
            throw new InvalidOperationException("Ship-check already running for this project.");
        }

        if (rebuildInProgress != 0)
        {
            throw new InvalidOperationException("Rebuild already running for this project.");
        }

        if (testsInProgress != 0)
        {
            throw new InvalidOperationException("Tests already running for this project.");
        }
    }

    public async Task<ControlPlaneRebuildResult> RunAgentRebuildAsync(
        string? configuration,
        CancellationToken cancellationToken)
    {
        EnsureNoOtherControlPlaneRun(
            Volatile.Read(ref shipCheckInProgress),
            Volatile.Read(ref agentRebuildInProgress),
            Volatile.Read(ref agentTestsInProgress));

        if (Interlocked.CompareExchange(ref agentRebuildInProgress, 1, 0) != 0)
        {
            throw new InvalidOperationException("Rebuild already running for this project.");
        }

        var wasRunning = runProcess?.IsRunning == true || watchPausedByControlPlane;
        shipCheckConfiguration = string.IsNullOrWhiteSpace(configuration) ? null : configuration.Trim();
        ControlPlaneRebuildResult? result = null;

        try
        {
            SetAgentRebuildPhase(ControlPlaneShipCheckPhase.Preparing);

            if (Volatile.Read(ref buildInProgress) != 0)
            {
                RequestBuildCancellation();
                await WaitForBuildIdleAsync(cancellationToken).ConfigureAwait(false);
            }

            await PauseWatchAsync(cancellationToken).ConfigureAwait(false);

            SetAgentRebuildPhase(ControlPlaneShipCheckPhase.Building);
            PrepareBuild("agent rebuild");
            await BuildAsync(cancellationToken).ConfigureAwait(false);

            var buildOk = lastBuildExitCode == 0;
            var projectLabel = definition.ProjectFile;
            var buildLogPath = logStore.GetLogPath(definition.Id, BuildLogKind.Build);
            var failures = new List<string>();
            if (!buildOk && !string.IsNullOrWhiteSpace(lastErrorPreview))
            {
                failures.Add(lastErrorPreview);
            }

            if (buildOk && RestartAppAfterRebuild && definition.RunOptions.RunMode != ProjectRunMode.None)
            {
                EnsureRunProcessStartedAfterBuild();
            }

            result = new ControlPlaneRebuildResult(
                Ok: buildOk,
                Project: projectLabel,
                Build: buildOk ? "pass" : "fail",
                ExitCode: lastBuildExitCode,
                Failures: failures,
                Log: buildLogPath);
            return result;
        }
        finally
        {
            shipCheckConfiguration = null;

            if (wasRunning && definition.RunOptions.RunMode != ProjectRunMode.None)
            {
                SetAgentRebuildPhase(ControlPlaneShipCheckPhase.ResumingWatch, immediate: true);
                ResumeWatch();
            }
            else
            {
                watchPausedByControlPlane = false;
            }

            Interlocked.Exchange(ref agentRebuildInProgress, 0);
            CompleteAgentRebuild(result?.Ok == true);
        }
    }

    public void SetSessionStore(ControlPlaneSessionStore store) => sessionStore = store;

    public void SetMetricsStore(ControlPlaneMetricsStore store) => metricsStore = store;

    public ControlPlaneWatchStatus GetWatchStatus()
    {
        if (runProcess?.IsRunning == true)
        {
            return new ControlPlaneWatchStatus(ControlPlaneWatchState.Running, runProcess.ProcessId);
        }

        if (watchPausedByControlPlane)
        {
            return new ControlPlaneWatchStatus(ControlPlaneWatchState.Paused, Pid: null);
        }

        return new ControlPlaneWatchStatus(ControlPlaneWatchState.Stopped, Pid: null);
    }

    public async Task<ControlPlaneWatchStatus> PauseWatchAsync(CancellationToken cancellationToken)
    {
        if (runProcess?.IsRunning == true)
        {
            watchPausedByControlPlane = true;
            await StopRunProcessAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (definition.RunOptions.RunMode != ProjectRunMode.None)
        {
            watchPausedByControlPlane = true;
        }

        return GetWatchStatus();
    }

    public ControlPlaneWatchStatus ResumeWatch()
    {
        if (watchPausedByControlPlane
            && definition.RunOptions.RunMode != ProjectRunMode.None
            && runProcess?.IsRunning != true)
        {
            StartRunProcess(skipEmbeddedBuild: true);
        }

        watchPausedByControlPlane = false;
        return GetWatchStatus();
    }

    public async Task<ControlPlaneRunStopResult> StopRunAsync(CancellationToken cancellationToken)
    {
        var wasRunning = runProcess?.IsRunning == true;
        var watch = await PauseWatchAsync(cancellationToken).ConfigureAwait(false);
        NotifyControlPlaneChanged(immediate: true);

        return new ControlPlaneRunStopResult(
            Ok: true,
            WasRunning: wasRunning,
            ExitCode: lastExitCode,
            Watch: watch);
    }

    public void RequestCancelInFlightBuild() => RequestBuildCancellation();

    public bool IsBuildInProgress => Volatile.Read(ref buildInProgress) != 0;

    public async Task WaitForBuildIdleAsync(CancellationToken cancellationToken)
    {
        while (Volatile.Read(ref buildInProgress) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<ControlPlaneShipCheckResult> RunShipCheckAsync(
        string? configuration,
        string? filter,
        CancellationToken cancellationToken)
    {
        EnsureNoOtherControlPlaneRun(
            Volatile.Read(ref shipCheckInProgress),
            Volatile.Read(ref agentRebuildInProgress),
            Volatile.Read(ref agentTestsInProgress));

        if (Interlocked.CompareExchange(ref shipCheckInProgress, 1, 0) != 0)
        {
            throw new InvalidOperationException("Ship-check already running for this project.");
        }

        var wasRunning = runProcess?.IsRunning == true || watchPausedByControlPlane;
        shipCheckConfiguration = string.IsNullOrWhiteSpace(configuration) ? null : configuration.Trim();
        shipCheckFilter = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
        ControlPlaneShipCheckResult? result = null;

        try
        {
            SetShipCheckPhase(ControlPlaneShipCheckPhase.Preparing);

            if (Volatile.Read(ref buildInProgress) != 0)
            {
                RequestBuildCancellation();
                await WaitForBuildIdleAsync(cancellationToken).ConfigureAwait(false);
            }

            await PauseWatchAsync(cancellationToken).ConfigureAwait(false);

            SetShipCheckPhase(ControlPlaneShipCheckPhase.Building);
            PrepareBuild("ship-check");
            await BuildAsync(cancellationToken).ConfigureAwait(false);

            var buildOk = lastBuildExitCode == 0;
            var projectLabel = definition.ProjectFile;
            var buildLogPath = logStore.GetLogPath(definition.Id, BuildLogKind.Build);
            var failures = new List<string>();

            if (!buildOk)
            {
                if (!string.IsNullOrWhiteSpace(lastErrorPreview))
                {
                    failures.Add(lastErrorPreview);
                }

                result = new ControlPlaneShipCheckResult(
                    Ok: false,
                    Project: projectLabel,
                    Build: "fail",
                    Tests: null,
                    Failures: failures,
                    Log: buildLogPath);
                return result;
            }

            var resolution = TestProjectDiscovery.Resolve(
                definition.RootFolder,
                definition.ProjectFile,
                definition.TestProjectFile);

            if (resolution.Targets.Count == 0)
            {
                result = new ControlPlaneShipCheckResult(
                    Ok: true,
                    Project: projectLabel,
                    Build: "pass",
                    Tests: null,
                    Failures: [],
                    Log: buildLogPath);
                return result;
            }

            SetShipCheckPhase(ControlPlaneShipCheckPhase.Testing);
            PrepareTest("ship-check");
            await TestAsync(cancellationToken).ConfigureAwait(false);

            var meta = await logStore.LoadMetadataAsync(definition.Id, BuildLogKind.Test, cancellationToken)
                .ConfigureAwait(false);
            var testLogPath = logStore.GetLogPath(definition.Id, BuildLogKind.Test);
            var testText = meta is null
                ? string.Empty
                : await logStore.LoadLogTextAsync(meta, maxBytes: 1_000_000, cancellationToken)
                    .ConfigureAwait(false);

            var summary = DotNetTestOutputParser.TryParseSummary(testText);
            var issues = DotNetTestOutputParser.ParseIssues(testText);
            foreach (var issue in issues.Where(i => i.IsError))
            {
                failures.Add(issue.Text);
            }

            var counts = summary is null
                ? new ControlPlaneTestCounts(
                    Failed: Snapshot.State == ProjectLifecycleState.TestOk ? 0 : 1,
                    Passed: 0,
                    Skipped: 0)
                : new ControlPlaneTestCounts(summary.Failed, summary.Passed, summary.Skipped);

            var testsOk = Snapshot.State == ProjectLifecycleState.TestOk && counts.Failed == 0;
            result = new ControlPlaneShipCheckResult(
                Ok: testsOk,
                Project: projectLabel,
                Build: "pass",
                Tests: counts,
                Failures: failures,
                Log: testLogPath);
            return result;
        }
        finally
        {
            shipCheckConfiguration = null;
            shipCheckFilter = null;

            if (wasRunning && definition.RunOptions.RunMode != ProjectRunMode.None)
            {
                SetShipCheckPhase(ControlPlaneShipCheckPhase.ResumingWatch, immediate: true);
                ResumeWatch();
            }
            else
            {
                watchPausedByControlPlane = false;
            }

            Interlocked.Exchange(ref shipCheckInProgress, 0);
            CompleteShipCheck(result?.Ok == true);
        }
    }

    private void CompleteAgentTests(bool ok)
    {
        lastAgentTestsOutcome = ok
            ? ControlPlaneShipCheckOutcome.Passed
            : ControlPlaneShipCheckOutcome.Failed;
        lastAgentTestsCompletedUtc = DateTimeOffset.UtcNow;
        NotifyControlPlaneChanged(immediate: true);
    }

    public async Task<ControlPlaneRunTestsResult> RunAgentTestsAsync(
        string? configuration,
        string? filter,
        CancellationToken cancellationToken)
    {
        EnsureNoOtherControlPlaneRun(
            Volatile.Read(ref shipCheckInProgress),
            Volatile.Read(ref agentRebuildInProgress),
            Volatile.Read(ref agentTestsInProgress));

        if (Volatile.Read(ref buildInProgress) != 0)
        {
            throw new InvalidOperationException("Build already running for this project.");
        }

        if (Interlocked.CompareExchange(ref agentTestsInProgress, 1, 0) != 0)
        {
            throw new InvalidOperationException("Tests already running for this project.");
        }

        shipCheckConfiguration = string.IsNullOrWhiteSpace(configuration) ? null : configuration.Trim();
        shipCheckFilter = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
        ControlPlaneRunTestsResult? result = null;

        try
        {
            NotifyControlPlaneChanged(immediate: true);
            PrepareTest("agent tests");
            await TestAsync(cancellationToken).ConfigureAwait(false);

            var projectLabel = definition.ProjectFile;
            var testLogPath = logStore.GetLogPath(definition.Id, BuildLogKind.Test);
            var failures = new List<string>();
            var meta = await logStore.LoadMetadataAsync(definition.Id, BuildLogKind.Test, cancellationToken)
                .ConfigureAwait(false);
            var testText = meta is null
                ? string.Empty
                : await logStore.LoadLogTextAsync(meta, maxBytes: 1_000_000, cancellationToken)
                    .ConfigureAwait(false);

            var summary = DotNetTestOutputParser.TryParseSummary(testText);
            var issues = DotNetTestOutputParser.ParseIssues(testText);
            foreach (var issue in issues.Where(i => i.IsError))
            {
                failures.Add(issue.Text);
            }

            var counts = summary is null
                ? new ControlPlaneTestCounts(
                    Failed: Snapshot.State == ProjectLifecycleState.TestOk ? 0 : 1,
                    Passed: 0,
                    Skipped: 0)
                : new ControlPlaneTestCounts(summary.Failed, summary.Passed, summary.Skipped);

            var testsOk = Snapshot.State == ProjectLifecycleState.TestOk && counts.Failed == 0;
            result = new ControlPlaneRunTestsResult(
                Ok: testsOk,
                Project: projectLabel,
                Tests: counts,
                Failures: failures,
                Log: testLogPath);
            return result;
        }
        finally
        {
            shipCheckConfiguration = null;
            shipCheckFilter = null;
            Interlocked.Exchange(ref agentTestsInProgress, 0);
            CompleteAgentTests(result?.Ok == true);
        }
    }

    private bool IsControlPlaneBusyBlockingAutoBuild() =>
        definition.BuildControlMode == ProjectBuildControlMode.FileWatching
        && sessionStore?.ShouldBlockAutoBuild(definition.Id) == true;

    public ProjectBuildControlMode GetBuildControlMode() => definition.BuildControlMode;

    /// <summary>
    /// Applies a build-control mode change. Cancels pending file-triggered schedules when entering AI Controlled;
    /// clears AI pending schedule state without building when returning to File Watching.
    /// </summary>
    public ControlPlaneModeStatus SetBuildControlMode(ProjectBuildControlMode mode)
    {
        var previous = definition.BuildControlMode;
        if (previous == mode)
        {
            return new ControlPlaneModeStatus(
                definition.Id,
                mode,
                ProjectBuildControlModeWire.ToWire(mode),
                previous,
                ProjectBuildControlModeWire.ToWire(previous));
        }

        definition.BuildControlMode = mode;

        if (mode == ProjectBuildControlMode.AiControlled)
        {
            // Cancel any pending file-triggered rebuild timer; keep observed change counts for the UI.
            Interlocked.Increment(ref fileChangeRebuildScheduleGeneration);
            Volatile.Write(ref pendingHotReloadRestartRequest, 0);
            if (pendingFileChangeRebuild)
            {
                pendingRebuildHoldReason = PendingRebuildHoldReason.EditsSettling;
            }

            if (state == ProjectLifecycleState.WaitingForEdits
                && Volatile.Read(ref buildInProgress) == 0)
            {
                SetState(runProcess?.IsRunning == true
                    ? ProjectLifecycleState.Running
                    : ProjectLifecycleState.Idle);
                SetProjectCurrentAction(
                    pendingFileChangeRebuild
                        ? "AI Controlled — changes awaiting explicit build"
                        : "AI Controlled — explicit build required");
            }

            // Drop dotnet watch so file edits cannot compile inside the host process.
            _ = MigrateRunHostForBuildControlModeAsync();
            TryStartFileWatcher();
        }
        else
        {
            // Leaving AI Controlled: do not surprise-build stale pending changes.
            Interlocked.Increment(ref fileChangeRebuildScheduleGeneration);
            pendingFileChangeRebuild = false;
            ClearPendingRebuildHold();
            if (state == ProjectLifecycleState.WaitingForEdits
                && Volatile.Read(ref buildInProgress) == 0)
            {
                SetState(ProjectLifecycleState.Idle);
                SetProjectCurrentAction("File Watching — waiting for next change");
            }

            // Restore watch/run strategy without building accumulated AI edits.
            _ = MigrateRunHostForBuildControlModeAsync();
        }

        NotifyControlPlaneChanged(immediate: true);
        return new ControlPlaneModeStatus(
            definition.Id,
            mode,
            ProjectBuildControlModeWire.ToWire(mode),
            previous,
            ProjectBuildControlModeWire.ToWire(previous));
    }

    /// <summary>
    /// Swaps the run host when build-control mode changes: AI Controlled uses
    /// <c>dotnet run --no-build</c>; File Watching may use <c>dotnet watch</c> again.
    /// Does not compile — only restarts the already-built host if one was running.
    /// </summary>
    private async Task MigrateRunHostForBuildControlModeAsync()
    {
        if (definition.RunOptions.RunMode == ProjectRunMode.None)
        {
            return;
        }

        if (Volatile.Read(ref buildInProgress) != 0
            || Volatile.Read(ref agentRebuildInProgress) != 0
            || Volatile.Read(ref shipCheckInProgress) != 0)
        {
            return;
        }

        var running = runProcess?.IsRunning == true;
        if (!running)
        {
            return;
        }

        var wantWatch = UsesDotNetWatchProcess();
        var isWatch = IsRunningDotNetWatchHost();
        if (wantWatch == isWatch)
        {
            return;
        }

        try
        {
            SetProjectCurrentAction(wantWatch
                ? "Switching host to dotnet watch (no rebuild)"
                : "Switching host to dotnet run --no-build (AI Controlled)");
            await StopRunProcessAsync(CancellationToken.None).ConfigureAwait(false);
            StartRunProcess(skipEmbeddedBuild: true);
        }
        catch
        {
            // Host migration is best-effort; explicit rebuild can recover.
        }
    }

    private bool IsRunningDotNetWatchHost()
    {
        var command = runProcess?.CommandLine;
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        return command.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains("watch", StringComparer.OrdinalIgnoreCase);
    }

    private void NoteAutoBuildBlockedByControlPlane() =>
        metricsStore?.RecordAutoBuildBlocked(definition.Id);

    private bool ShouldSkipAutoBuildTests() =>
        Volatile.Read(ref shipCheckInProgress) != 0
        || Volatile.Read(ref agentRebuildInProgress) != 0
        || Volatile.Read(ref agentTestsInProgress) != 0
        || sessionStore?.ShouldSuppressAutoBuildTests(definition.Id) == true;
}
