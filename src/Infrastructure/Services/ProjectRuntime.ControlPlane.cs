using BuildMonitor.Core.Models;
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
        if (Interlocked.CompareExchange(ref shipCheckInProgress, 1, 0) != 0)
        {
            throw new InvalidOperationException("Ship-check already running for this project.");
        }

        var wasRunning = runProcess?.IsRunning == true || watchPausedByControlPlane;
        shipCheckConfiguration = string.IsNullOrWhiteSpace(configuration) ? null : configuration.Trim();
        shipCheckFilter = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();

        try
        {
            if (Volatile.Read(ref buildInProgress) != 0)
            {
                RequestBuildCancellation();
                await WaitForBuildIdleAsync(cancellationToken).ConfigureAwait(false);
            }

            await PauseWatchAsync(cancellationToken).ConfigureAwait(false);

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

                return new ControlPlaneShipCheckResult(
                    Ok: false,
                    Project: projectLabel,
                    Build: "fail",
                    Tests: null,
                    Failures: failures,
                    Log: buildLogPath);
            }

            var resolution = TestProjectDiscovery.Resolve(
                definition.RootFolder,
                definition.ProjectFile,
                definition.TestProjectFile);

            if (resolution.Targets.Count == 0)
            {
                // No test project configured/discovered — ok follows build only.
                return new ControlPlaneShipCheckResult(
                    Ok: true,
                    Project: projectLabel,
                    Build: "pass",
                    Tests: null,
                    Failures: [],
                    Log: buildLogPath);
            }

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
            return new ControlPlaneShipCheckResult(
                Ok: testsOk,
                Project: projectLabel,
                Build: "pass",
                Tests: counts,
                Failures: failures,
                Log: testLogPath);
        }
        finally
        {
            shipCheckConfiguration = null;
            shipCheckFilter = null;
            Interlocked.Exchange(ref shipCheckInProgress, 0);

            if (wasRunning && definition.RunOptions.RunMode != ProjectRunMode.None)
            {
                ResumeWatch();
            }
            else
            {
                watchPausedByControlPlane = false;
            }
        }
    }

    private bool IsControlPlaneBusyBlockingAutoBuild() =>
        sessionStore?.ShouldBlockAutoBuild(definition.Id) == true;

    private void NoteAutoBuildBlockedByControlPlane() =>
        metricsStore?.RecordAutoBuildBlocked(definition.Id);

    private bool ShouldSkipAutoBuildTests() =>
        Volatile.Read(ref shipCheckInProgress) != 0
        || sessionStore?.ShouldSuppressAutoBuildTests(definition.Id) == true;
}
