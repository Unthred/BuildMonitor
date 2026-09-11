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
    private ControlPlaneOperationLease? activeControlPlaneLease;
    private bool agentBuildEndedByTokenCancel;
    private bool agentTestEndedByTokenCancel;
    private readonly object controlPlaneOperationSync = new();

    public void NotifyControlPlaneChanged(bool immediate = true)
    {
        MarkHealthDirty();
        HealthCoalesceRequested?.Invoke(immediate);
    }

    public ProjectControlPlaneSnapshot BuildControlPlaneSnapshot(DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        var sessionStatus = sessionStore?.GetStatus(projectSettings.Id, now);
        var sessionApiUsed = sessionStatus?.SessionApiUsed == true;
        var effectiveState = sessionStatus?.State ?? ControlPlaneSessionState.Idle;
        var autoBuildEnabled = !BuildTriggerPolicy.IsAutoBuildDisabledByMode(Local.BuildControlMode);
        var autoBuildBlocked = !BuildTriggerPolicy.ShouldAutoBuildFromFileChange(
            Local.BuildControlMode,
            sessionStatus?.SessionApiUsed == true,
            effectiveState);
        var inShipCheck = Volatile.Read(ref shipCheckInProgress) != 0;
        var inRebuild = Volatile.Read(ref agentRebuildInProgress) != 0;
        var lease = activeControlPlaneLease;

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
            BuildControlMode: Local.BuildControlMode,
            AutoBuildEnabled: autoBuildEnabled,
            ActiveOperationId: lease?.OperationId,
            ActiveOperationKind: lease?.Kind,
            OperationCancelRequested: lease?.CancelRequested == true);
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
        var lease = TryAcquireControlPlaneLease(ControlPlaneOperationKind.Rebuild);
        var shouldResume = RunHostLifecyclePolicy.ShouldResumeHostAfterOperation(
            desiredRunHostState,
            Local.RunOptions.RunMode);
        shipCheckConfiguration = string.IsNullOrWhiteSpace(configuration) ? null : configuration.Trim();
        ControlPlaneRebuildResult? result = null;
        string? historyOpId = null;
        agentBuildEndedByTokenCancel = false;

        try
        {
            SetAgentRebuildPhase(ControlPlaneShipCheckPhase.Preparing);

            if (Volatile.Read(ref buildInProgress) != 0)
            {
                RequestBuildCancellation();
                await WaitForBuildIdleAsync(cancellationToken).ConfigureAwait(false);
            }

            if (lease.CancelRequested)
            {
                result = CreateCancelledRebuildResult(Local.ProjectFile);
                return result;
            }

            // Begin correlation only after any prior build has finished so we do not steal its OperationId.
            if (!TryBeginHistoryOperation(
                    OperationalEventSource.Agent,
                    "rebuild",
                    "Rebuild requested",
                    out var begunOp,
                    preferredOperationId: lease.OperationId))
            {
                throw new InvalidOperationException(
                    "Another operational history operation is already active for this project.");
            }

            historyOpId = begunOp;

            await PauseWatchAsync(cancellationToken).ConfigureAwait(false);

            if (lease.CancelRequested)
            {
                result = CreateCancelledRebuildResult(Local.ProjectFile);
                return result;
            }

            SetAgentRebuildPhase(ControlPlaneShipCheckPhase.Building);
            PrepareBuild("agent rebuild");
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                       lease.Token,
                       cancellationToken))
            {
                await BuildAsync(linked.Token).ConfigureAwait(false);
            }

            // Late CancelRequested after a normal process exit must not overwrite success/failure.
            if (ControlPlaneCancelClassification.TryClassifyCancelledBuild(
                    lease.CancelRequested,
                    agentBuildEndedByTokenCancel) is not null)
            {
                result = CreateCancelledRebuildResult(Local.ProjectFile);
                return result;
            }

            if (agentBuildEndedByTokenCancel)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new OperationCanceledException(cancellationToken);
            }

            var buildOk = lastBuildExitCode == 0;
            var projectLabel = Local.ProjectFile;
            var buildLogPath = logStore.GetLogPath(projectSettings.Id, BuildLogKind.Build);
            var failures = new List<string>();
            if (!buildOk && !string.IsNullOrWhiteSpace(lastErrorPreview))
            {
                failures.Add(lastErrorPreview);
            }

            // RestartAppAfterRebuild only restores when desired host state is Running (central gate).
            if (buildOk && RestartAppAfterRebuild && shouldResume)
            {
                EnsureRunProcessStartedAfterBuild();
            }

            result = new ControlPlaneRebuildResult(
                Ok: buildOk,
                Project: projectLabel,
                Build: buildOk ? "pass" : "fail",
                ExitCode: lastBuildExitCode,
                Failures: failures,
                Log: buildLogPath,
                Outcome: ControlPlaneOperationOutcomeMapper.FromRebuild(buildOk));
            return result;
        }
        catch (OperationCanceledException) when (
            lease.CancelRequested
            && (agentBuildEndedByTokenCancel || result is null))
        {
            result = CreateCancelledRebuildResult(Local.ProjectFile);
            return result;
        }
        finally
        {
            shipCheckConfiguration = null;

            // Step 1: terminal — no longer cancellable; keep exclusivity through finalization.
            RetireControlPlaneCancellationTarget(lease);

            if (shouldResume)
            {
                SetAgentRebuildPhase(ControlPlaneShipCheckPhase.ResumingWatch, immediate: true);
                ResumeWatch();
            }
            else
            {
                watchPausedByControlPlane = false;
            }

            CompleteAgentRebuildForOutcome(result?.Outcome);
            if (historyOpId is not null && result?.Outcome == ControlPlaneOperationOutcome.Cancelled)
            {
                history.RecordExplicit(
                    OperationalEventSource.Agent,
                    "rebuild",
                    "Rebuild cancelled",
                    OperationalEventOutcome.Cancelled);
            }

            EndHistoryOperation(historyOpId);

            // Step 3: fully finalized — release exclusivity so a new /run/* may start.
            ReleaseControlPlaneOperationExclusivity(lease, ControlPlaneOperationKind.Rebuild);
        }
    }

    public ControlPlaneCancelResult RequestCancelControlPlaneOperation(string? operationId)
    {
        ControlPlaneOperationLease lease;
        lock (controlPlaneOperationSync)
        {
            lease = activeControlPlaneLease
                    ?? throw new InvalidOperationException(
                        "No cancellable control-plane operation is active for this project.");

            if (!string.IsNullOrWhiteSpace(operationId)
                && !string.Equals(operationId.Trim(), lease.OperationId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "operationId does not match the current control-plane operation.");
            }

            var already = lease.RequestCancel();
            // Also cancel in-flight edit-gating-linked build CTS when this lease owns the build.
            RequestBuildCancellation();
            NotifyControlPlaneChanged(immediate: true);
            return new ControlPlaneCancelResult(
                Ok: true,
                Project: Local.ProjectFile,
                OperationId: lease.OperationId,
                OperationKind: lease.Kind,
                CancelRequested: true,
                AlreadyRequested: already);
        }
    }

    private ControlPlaneOperationLease TryAcquireControlPlaneLease(ControlPlaneOperationKind kind)
    {
        lock (controlPlaneOperationSync)
        {
            if (activeControlPlaneLease is not null)
            {
                throw new InvalidOperationException(
                    "A control-plane operation lease is still active for this project.");
            }

            EnsureNoOtherControlPlaneRun(
                Volatile.Read(ref shipCheckInProgress),
                Volatile.Read(ref agentRebuildInProgress),
                Volatile.Read(ref agentTestsInProgress));

            switch (kind)
            {
                case ControlPlaneOperationKind.Rebuild:
                    if (Interlocked.CompareExchange(ref agentRebuildInProgress, 1, 0) != 0)
                    {
                        throw new InvalidOperationException("Rebuild already running for this project.");
                    }

                    break;
                case ControlPlaneOperationKind.Tests:
                    if (Volatile.Read(ref buildInProgress) != 0)
                    {
                        throw new InvalidOperationException("Build already running for this project.");
                    }

                    if (Interlocked.CompareExchange(ref agentTestsInProgress, 1, 0) != 0)
                    {
                        throw new InvalidOperationException("Tests already running for this project.");
                    }

                    break;
                case ControlPlaneOperationKind.ShipCheck:
                    if (Interlocked.CompareExchange(ref shipCheckInProgress, 1, 0) != 0)
                    {
                        throw new InvalidOperationException("Ship-check already running for this project.");
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }

            var lease = new ControlPlaneOperationLease(kind);
            activeControlPlaneLease = lease;
            NotifyControlPlaneChanged(immediate: true);
            return lease;
        }
    }

    /// <summary>
    /// Step 1 of terminal finalization: clear the cancel target so <c>/run/cancel</c> returns 409,
    /// while keeping the exclusive in-progress flag so a new <c>/run/*</c> cannot start yet.
    /// Allowed intermediate state: <c>lease == null</c> and exclusive flag still <c>1</c>.
    /// </summary>
    private void RetireControlPlaneCancellationTarget(ControlPlaneOperationLease? lease)
    {
        lock (controlPlaneOperationSync)
        {
            if (lease is not null && ReferenceEquals(activeControlPlaneLease, lease))
            {
                activeControlPlaneLease = null;
            }
        }

        NotifyControlPlaneChanged(immediate: true);
    }

    /// <summary>
    /// Step 3 of terminal finalization: after resume/history/completion work, clear exclusivity
    /// and dispose the retired lease so a new operation may acquire.
    /// </summary>
    private void ReleaseControlPlaneOperationExclusivity(
        ControlPlaneOperationLease? lease,
        ControlPlaneOperationKind kind)
    {
        lock (controlPlaneOperationSync)
        {
            // Never leave an old lease installed once exclusivity is released.
            if (lease is not null && ReferenceEquals(activeControlPlaneLease, lease))
            {
                activeControlPlaneLease = null;
            }

            switch (kind)
            {
                case ControlPlaneOperationKind.Rebuild:
                    Interlocked.Exchange(ref agentRebuildInProgress, 0);
                    break;
                case ControlPlaneOperationKind.Tests:
                    Interlocked.Exchange(ref agentTestsInProgress, 0);
                    break;
                case ControlPlaneOperationKind.ShipCheck:
                    Interlocked.Exchange(ref shipCheckInProgress, 0);
                    break;
            }
        }

        lease?.Dispose();
        NotifyControlPlaneChanged(immediate: true);
    }

    private static ControlPlaneRebuildResult CreateCancelledRebuildResult(string projectLabel) =>
        new(
            Ok: false,
            Project: projectLabel,
            Build: "cancelled",
            ExitCode: -1,
            Failures: [],
            Log: null,
            Outcome: ControlPlaneOperationOutcomeMapper.Cancelled());

    private static ControlPlaneRunTestsResult CreateCancelledTestsResult(string projectLabel, string? log) =>
        new(
            Ok: false,
            Project: projectLabel,
            Tests: null,
            Failures: [],
            Log: log,
            Outcome: ControlPlaneOperationOutcomeMapper.Cancelled());

    private static ControlPlaneShipCheckResult CreateCancelledShipCheckResult(
        string projectLabel,
        string build,
        string? log) =>
        new(
            Ok: false,
            Project: projectLabel,
            Build: build,
            Tests: null,
            Failures: [],
            Log: log,
            Outcome: ControlPlaneOperationOutcomeMapper.Cancelled());

    private void CompleteAgentRebuildForOutcome(ControlPlaneOperationOutcome? outcome)
    {
        if (outcome == ControlPlaneOperationOutcome.Cancelled)
        {
            lastAgentRebuildOutcome = ControlPlaneShipCheckOutcome.None;
            lastAgentRebuildCompletedUtc = null;
            agentRebuildPhase = ControlPlaneShipCheckPhase.None;
            NotifyControlPlaneChanged(immediate: true);
            return;
        }

        CompleteAgentRebuild(outcome == ControlPlaneOperationOutcome.Succeeded);
    }

    private void CompleteAgentTestsForOutcome(ControlPlaneOperationOutcome? outcome)
    {
        if (outcome == ControlPlaneOperationOutcome.Cancelled)
        {
            lastAgentTestsOutcome = ControlPlaneShipCheckOutcome.None;
            lastAgentTestsCompletedUtc = null;
            NotifyControlPlaneChanged(immediate: true);
            return;
        }

        CompleteAgentTests(outcome == ControlPlaneOperationOutcome.Succeeded);
    }

    private void CompleteShipCheckForOutcome(ControlPlaneOperationOutcome? outcome)
    {
        if (outcome == ControlPlaneOperationOutcome.Cancelled)
        {
            lastShipCheckOutcome = ControlPlaneShipCheckOutcome.None;
            lastShipCheckCompletedUtc = null;
            shipCheckPhase = ControlPlaneShipCheckPhase.None;
            NotifyControlPlaneChanged(immediate: true);
            return;
        }

        CompleteShipCheck(outcome == ControlPlaneOperationOutcome.Succeeded);
    }

    public void SetSessionStore(ControlPlaneSessionStore store) => sessionStore = store;

    public void SetMetricsStore(ControlPlaneMetricsStore store) => metricsStore = store;

    public ControlPlaneWatchStatus GetWatchStatus()
    {
        if (runProcess?.IsRunning == true)
        {
            return new ControlPlaneWatchStatus(ControlPlaneWatchState.Running, runProcess.ProcessId);
        }

        if (desiredRunHostState == DesiredRunHostState.Stopped)
        {
            return new ControlPlaneWatchStatus(ControlPlaneWatchState.Stopped, Pid: null);
        }

        if (watchPausedByControlPlane)
        {
            return new ControlPlaneWatchStatus(ControlPlaneWatchState.Paused, Pid: null);
        }

        return new ControlPlaneWatchStatus(ControlPlaneWatchState.Stopped, Pid: null);
    }

    public async Task<ControlPlaneWatchStatus> PauseWatchAsync(CancellationToken cancellationToken)
    {
        // Temporary operational pause — does not change desired host state.
        if (runProcess?.IsRunning == true)
        {
            watchPausedByControlPlane = true;
            await StopRunProcessAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (desiredRunHostState == DesiredRunHostState.Running
                 && Local.RunOptions.RunMode != ProjectRunMode.None)
        {
            watchPausedByControlPlane = true;
        }

        return GetWatchStatus();
    }

    public ControlPlaneWatchStatus ResumeWatch()
    {
        if (RunHostLifecyclePolicy.ShouldResumeHostAfterOperation(
                desiredRunHostState,
                Local.RunOptions.RunMode)
            && watchPausedByControlPlane
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
        desiredRunHostState = DesiredRunHostState.Stopped;
        watchPausedByControlPlane = false;
        await StopRunProcessAsync(cancellationToken).ConfigureAwait(false);
        if (wasRunning)
        {
            history.RecordHostStopped("Host stopped");
        }
        if (Local.RunOptions.RunMode != ProjectRunMode.None)
        {
            SetState(ProjectLifecycleState.Idle);
        }

        NotifyControlPlaneChanged(immediate: true);

        return new ControlPlaneRunStopResult(
            Ok: true,
            WasRunning: wasRunning,
            ExitCode: lastExitCode,
            Watch: GetWatchStatus());
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
        var lease = TryAcquireControlPlaneLease(ControlPlaneOperationKind.ShipCheck);
        var shouldResume = RunHostLifecyclePolicy.ShouldResumeHostAfterOperation(
            desiredRunHostState,
            Local.RunOptions.RunMode);
        shipCheckConfiguration = string.IsNullOrWhiteSpace(configuration) ? null : configuration.Trim();
        shipCheckFilter = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
        ControlPlaneShipCheckResult? result = null;
        string? historyOpId = null;
        agentBuildEndedByTokenCancel = false;
        agentTestEndedByTokenCancel = false;

        try
        {
            SetShipCheckPhase(ControlPlaneShipCheckPhase.Preparing);

            if (Volatile.Read(ref buildInProgress) != 0)
            {
                RequestBuildCancellation();
                await WaitForBuildIdleAsync(cancellationToken).ConfigureAwait(false);
            }

            if (lease.CancelRequested)
            {
                result = CreateCancelledShipCheckResult(Local.ProjectFile, "cancelled", null);
                return result;
            }

            if (!TryBeginHistoryOperation(
                    OperationalEventSource.Agent,
                    "ship-check",
                    "Ship-check requested",
                    out var begunOp,
                    preferredOperationId: lease.OperationId))
            {
                throw new InvalidOperationException(
                    "Another operational history operation is already active for this project.");
            }

            historyOpId = begunOp;

            await PauseWatchAsync(cancellationToken).ConfigureAwait(false);

            if (lease.CancelRequested)
            {
                result = CreateCancelledShipCheckResult(Local.ProjectFile, "cancelled", null);
                return result;
            }

            SetShipCheckPhase(ControlPlaneShipCheckPhase.Building);
            PrepareBuild("ship-check");
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                       lease.Token,
                       cancellationToken))
            {
                await BuildAsync(linked.Token).ConfigureAwait(false);
            }

            if (ControlPlaneCancelClassification.TryClassifyCancelledBuild(
                    lease.CancelRequested,
                    agentBuildEndedByTokenCancel) is not null)
            {
                result = CreateCancelledShipCheckResult(
                    Local.ProjectFile,
                    "cancelled",
                    logStore.GetLogPath(projectSettings.Id, BuildLogKind.Build));
                return result;
            }

            if (agentBuildEndedByTokenCancel)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new OperationCanceledException(cancellationToken);
            }

            var buildOk = lastBuildExitCode == 0;
            var projectLabel = Local.ProjectFile;
            var buildLogPath = logStore.GetLogPath(projectSettings.Id, BuildLogKind.Build);
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
                    Log: buildLogPath,
                    Outcome: ControlPlaneOperationOutcomeMapper.FromShipCheckBuildOnly(buildOk: false));
                return result;
            }

            // Between phases: cancel may skip tests without token-owned build termination.
            if (ControlPlaneCancelClassification.ShouldSkipNextPhaseDueToCancel(lease.CancelRequested))
            {
                result = CreateCancelledShipCheckResult(projectLabel, "pass", buildLogPath);
                return result;
            }

            var resolution = TestProjectDiscovery.Resolve(
                Local.RootFolder,
                Local.ProjectFile,
                Local.TestProjectFile);

            if (resolution.Targets.Count == 0)
            {
                result = new ControlPlaneShipCheckResult(
                    Ok: true,
                    Project: projectLabel,
                    Build: "pass",
                    Tests: null,
                    Failures: [],
                    Log: buildLogPath,
                    Outcome: ControlPlaneOperationOutcomeMapper.FromShipCheck(
                        buildOk: true,
                        noTestTargetsConfigured: true,
                        testEvidence: null));
                return result;
            }

            if (ControlPlaneCancelClassification.ShouldSkipNextPhaseDueToCancel(lease.CancelRequested))
            {
                result = CreateCancelledShipCheckResult(projectLabel, "pass", buildLogPath);
                return result;
            }

            SetShipCheckPhase(ControlPlaneShipCheckPhase.Testing);
            PrepareTest("ship-check");
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                       lease.Token,
                       cancellationToken))
            {
                await TestAsync(linked.Token).ConfigureAwait(false);
            }

            if (ControlPlaneCancelClassification.TryClassifyCancelledTests(
                    lease.CancelRequested,
                    agentTestEndedByTokenCancel) is not null)
            {
                result = CreateCancelledShipCheckResult(
                    projectLabel,
                    "pass",
                    logStore.GetLogPath(projectSettings.Id, BuildLogKind.Test));
                return result;
            }

            if (agentTestEndedByTokenCancel)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new OperationCanceledException(cancellationToken);
            }

            var meta = await logStore.LoadMetadataAsync(projectSettings.Id, BuildLogKind.Test, cancellationToken)
                .ConfigureAwait(false);
            var testLogPath = logStore.GetLogPath(projectSettings.Id, BuildLogKind.Test);
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

            var (testsOk, counts, evidence) = ControlPlaneTestResultMapper.MapCompletedTestPhase(
                summary,
                lifecycleTestOk: Snapshot.State == ProjectLifecycleState.TestOk,
                failures,
                noTargetsConfigured: false);
            result = new ControlPlaneShipCheckResult(
                Ok: testsOk,
                Project: projectLabel,
                Build: "pass",
                Tests: counts,
                Failures: failures,
                Log: testLogPath,
                Outcome: ControlPlaneOperationOutcomeMapper.FromShipCheck(
                    buildOk: true,
                    noTestTargetsConfigured: false,
                    testEvidence: evidence));
            return result;
        }
        catch (OperationCanceledException) when (
            lease.CancelRequested
            && (agentBuildEndedByTokenCancel || agentTestEndedByTokenCancel || result is null))
        {
            result = CreateCancelledShipCheckResult(Local.ProjectFile, "cancelled", null);
            return result;
        }
        finally
        {
            shipCheckConfiguration = null;
            shipCheckFilter = null;

            RetireControlPlaneCancellationTarget(lease);

            if (shouldResume)
            {
                SetShipCheckPhase(ControlPlaneShipCheckPhase.ResumingWatch, immediate: true);
                ResumeWatch();
            }
            else
            {
                watchPausedByControlPlane = false;
            }

            CompleteShipCheckForOutcome(result?.Outcome);
            if (historyOpId is not null)
            {
                if (result?.Outcome == ControlPlaneOperationOutcome.Cancelled)
                {
                    history.RecordExplicit(
                        OperationalEventSource.Agent,
                        "ship-check",
                        "Ship-check cancelled",
                        OperationalEventOutcome.Cancelled);
                }
                else
                {
                    history.RecordExplicit(
                        OperationalEventSource.Agent,
                        "ship-check",
                        result?.Ok == true ? "Ship-check completed" : "Ship-check completed with failure",
                        result?.Ok == true
                            ? OperationalEventOutcome.Succeeded
                            : OperationalEventOutcome.Failed);
                }
            }

            EndHistoryOperation(historyOpId);
            ReleaseControlPlaneOperationExclusivity(lease, ControlPlaneOperationKind.ShipCheck);
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
        var lease = TryAcquireControlPlaneLease(ControlPlaneOperationKind.Tests);
        shipCheckConfiguration = string.IsNullOrWhiteSpace(configuration) ? null : configuration.Trim();
        shipCheckFilter = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
        ControlPlaneRunTestsResult? result = null;
        string? historyOpId = null;
        agentTestEndedByTokenCancel = false;

        try
        {
            if (lease.CancelRequested)
            {
                result = CreateCancelledTestsResult(Local.ProjectFile, null);
                return result;
            }

            if (!TryBeginHistoryOperation(
                    OperationalEventSource.Agent,
                    "tests",
                    "Tests requested",
                    out var begunOp,
                    preferredOperationId: lease.OperationId))
            {
                throw new InvalidOperationException(
                    "Another operational history operation is already active for this project.");
            }

            historyOpId = begunOp;

            NotifyControlPlaneChanged(immediate: true);

            var resolution = TestProjectDiscovery.Resolve(
                Local.RootFolder,
                Local.ProjectFile,
                Local.TestProjectFile);
            var noTargetsConfigured = resolution.Targets.Count == 0;

            if (lease.CancelRequested)
            {
                result = CreateCancelledTestsResult(Local.ProjectFile, null);
                return result;
            }

            PrepareTest("agent tests");
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                       lease.Token,
                       cancellationToken))
            {
                await TestAsync(linked.Token).ConfigureAwait(false);
            }

            if (ControlPlaneCancelClassification.TryClassifyCancelledTests(
                    lease.CancelRequested,
                    agentTestEndedByTokenCancel) is not null)
            {
                result = CreateCancelledTestsResult(
                    Local.ProjectFile,
                    logStore.GetLogPath(projectSettings.Id, BuildLogKind.Test));
                return result;
            }

            if (agentTestEndedByTokenCancel)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new OperationCanceledException(cancellationToken);
            }

            var projectLabel = Local.ProjectFile;
            var testLogPath = logStore.GetLogPath(projectSettings.Id, BuildLogKind.Test);
            var failures = new List<string>();
            var meta = await logStore.LoadMetadataAsync(projectSettings.Id, BuildLogKind.Test, cancellationToken)
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

            var (testsOk, counts, evidence) = ControlPlaneTestResultMapper.MapCompletedTestPhase(
                summary,
                lifecycleTestOk: Snapshot.State == ProjectLifecycleState.TestOk,
                failures,
                noTargetsConfigured);
            result = new ControlPlaneRunTestsResult(
                Ok: testsOk,
                Project: projectLabel,
                Tests: counts,
                Failures: failures,
                Log: testLogPath,
                Outcome: ControlPlaneOperationOutcomeMapper.FromTests(evidence));
            return result;
        }
        catch (OperationCanceledException) when (
            lease.CancelRequested
            && (agentTestEndedByTokenCancel || result is null))
        {
            result = CreateCancelledTestsResult(Local.ProjectFile, null);
            return result;
        }
        finally
        {
            shipCheckConfiguration = null;
            shipCheckFilter = null;
            RetireControlPlaneCancellationTarget(lease);
            CompleteAgentTestsForOutcome(result?.Outcome);
            if (historyOpId is not null && result?.Outcome == ControlPlaneOperationOutcome.Cancelled)
            {
                history.RecordExplicit(
                    OperationalEventSource.Agent,
                    "tests",
                    "Tests cancelled",
                    OperationalEventOutcome.Cancelled);
            }

            EndHistoryOperation(historyOpId);
            ReleaseControlPlaneOperationExclusivity(lease, ControlPlaneOperationKind.Tests);
        }
    }

    private bool IsControlPlaneBusyBlockingAutoBuild() =>
        Local.BuildControlMode == ProjectBuildControlMode.FileWatching
        && sessionStore?.ShouldBlockAutoBuild(projectSettings.Id) == true;

    public ProjectBuildControlMode GetBuildControlMode() => Local.BuildControlMode;

    /// <summary>
    /// Applies a build-control mode change. Cancels pending file-triggered schedules when entering AI Controlled;
    /// clears AI pending schedule state without building when returning to File Watching.
    /// </summary>
    public ControlPlaneModeStatus SetBuildControlMode(ProjectBuildControlMode mode)
    {
        var previous = Local.BuildControlMode;
        if (previous == mode)
        {
            return new ControlPlaneModeStatus(
                projectSettings.Id,
                mode,
                ProjectBuildControlModeWire.ToWire(mode),
                previous,
                ProjectBuildControlModeWire.ToWire(previous));
        }

        Local.BuildControlMode = mode;

        history.RecordWorkflowModeChange(
            OperationalEventSource.Agent,
            ProjectBuildControlModeWire.ToWire(previous),
            ProjectBuildControlModeWire.ToWire(mode));

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
            projectSettings.Id,
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
        if (Local.RunOptions.RunMode == ProjectRunMode.None)
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
        metricsStore?.RecordAutoBuildBlocked(projectSettings.Id);

    private bool ShouldSkipAutoBuildTests() =>
        Volatile.Read(ref shipCheckInProgress) != 0
        || Volatile.Read(ref agentRebuildInProgress) != 0
        || Volatile.Read(ref agentTestsInProgress) != 0
        || sessionStore?.ShouldSuppressAutoBuildTests(projectSettings.Id) == true;
}
