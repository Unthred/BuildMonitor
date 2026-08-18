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
    private ControlPlaneShipCheckPhase shipCheckPhase = ControlPlaneShipCheckPhase.None;
    private ControlPlaneShipCheckPhase agentRebuildPhase = ControlPlaneShipCheckPhase.None;
    private ControlPlaneShipCheckOutcome lastShipCheckOutcome = ControlPlaneShipCheckOutcome.None;
    private ControlPlaneShipCheckOutcome lastAgentRebuildOutcome = ControlPlaneShipCheckOutcome.None;
    private DateTimeOffset? lastShipCheckCompletedUtc;
    private DateTimeOffset? lastAgentRebuildCompletedUtc;
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
        var autoBuildBlocked = sessionStore?.ShouldBlockAutoBuild(definition.Id, now) == true;
        var inShipCheck = Volatile.Read(ref shipCheckInProgress) != 0;
        var inRebuild = Volatile.Read(ref agentRebuildInProgress) != 0;

        return new ProjectControlPlaneSnapshot(
            SessionApiUsed: sessionApiUsed,
            EffectiveSessionState: effectiveState,
            SessionSinceUtc: sessionStatus?.Since,
            AutoBuildBlockedBySession: autoBuildBlocked,
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
            LastAgentRebuildCompletedUtc: lastAgentRebuildCompletedUtc);
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

    private static void EnsureNoOtherControlPlaneRun(int shipCheckInProgress, int rebuildInProgress)
    {
        if (shipCheckInProgress != 0)
        {
            throw new InvalidOperationException("Ship-check already running for this project.");
        }

        if (rebuildInProgress != 0)
        {
            throw new InvalidOperationException("Rebuild already running for this project.");
        }
    }

    public async Task<ControlPlaneRebuildResult> RunAgentRebuildAsync(
        string? configuration,
        CancellationToken cancellationToken)
    {
        EnsureNoOtherControlPlaneRun(
            Volatile.Read(ref shipCheckInProgress),
            Volatile.Read(ref agentRebuildInProgress));

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
            Volatile.Read(ref agentRebuildInProgress));

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

    private bool IsControlPlaneBusyBlockingAutoBuild() =>
        sessionStore?.ShouldBlockAutoBuild(definition.Id) == true;

    private void NoteAutoBuildBlockedByControlPlane() =>
        metricsStore?.RecordAutoBuildBlocked(definition.Id);

    private bool ShouldSkipAutoBuildTests() =>
        Volatile.Read(ref shipCheckInProgress) != 0
        || Volatile.Read(ref agentRebuildInProgress) != 0
        || sessionStore?.ShouldSuppressAutoBuildTests(definition.Id) == true;
}
