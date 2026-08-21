using System.Text;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Infrastructure.Services;

internal sealed partial class ProjectRuntime
{
    public async Task TestAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref testInProgress, 1, 0) != 0)
        {
            notifyUser?.Invoke(
                projectSettings.Id,
                $"Tests skipped — {projectSettings.DisplayName}",
                "Tests are already running for this project.",
                UserNotificationKind.Warning,
                UserNotificationCategory.Warning);
            return;
        }

        if (Volatile.Read(ref buildInProgress) != 0)
        {
            Interlocked.Exchange(ref testInProgress, 0);
            notifyUser?.Invoke(
                projectSettings.Id,
                $"Tests skipped — {projectSettings.DisplayName}",
                "Wait for the current build to finish, then try again.",
                UserNotificationKind.Warning,
                UserNotificationCategory.Warning);
            return;
        }

        var testReason = pendingTestReason;
        pendingTestReason = "tests";
        var wasRunProcessActive = runProcess?.IsRunning == true;
        var releaseLocksSetting = Local.RunOptions.ReleaseOutputLocksBeforeBuild;
        var stoppedAppForTests = false;
        var preservedBuildErrors = buildErrorCount;
        var preservedBuildWarnings = buildWarningCount;

        fileWatcher?.Suspend();
        fileChangeBuildCooldownUntil = DateTimeOffset.UtcNow.AddMinutes(2);

        try
        {
            lock (liveOutputSync)
            {
                liveTestOutput.Clear();
            }

            Interlocked.Exchange(ref liveTestOutputRevision, 0);
            buildErrorCount = 0;
            buildWarningCount = 0;
            lastErrorPreview = null;

            var resolution = TestProjectDiscovery.Resolve(
                Local.RootFolder,
                Local.ProjectFile,
                Local.TestProjectFile);

            if (resolution.Targets.Count == 0)
            {
                WriteTestStartBanner(testReason, [], resolution.DiscoveryNote);
                SetState(ProjectLifecycleState.TestFailed);
                lastErrorPreview = resolution.DiscoveryNote;
                buildErrorCount = 1;
                return;
            }

            WriteTestStartBanner(testReason, resolution);
            SetState(ProjectLifecycleState.Testing);
            NotifyProgressChanged(force: true);

            var startedAtUtc = DateTimeOffset.UtcNow;
            var commandLines = new List<string>();
            var exitCode = 0;
            var wallDuration = TimeSpan.Zero;

            for (var i = 0; i < resolution.Targets.Count; i++)
            {
                var target = resolution.Targets[i];
                if (resolution.Targets.Count > 1)
                {
                    AppendTestSectionHeader(i + 1, resolution.Targets.Count, target);
                }

                var targetRun = await RunTestTargetWithRetryAsync(
                    target,
                    wasRunProcessActive,
                    releaseLocksSetting,
                    cancellationToken);

                stoppedAppForTests |= targetRun.StoppedApp;
                commandLines.Add(targetRun.Result.CommandLine);
                wallDuration += targetRun.Result.Duration;
                if (targetRun.Result.ExitCode != 0)
                {
                    exitCode = targetRun.Result.ExitCode;
                }
            }

            string logText;
            lock (liveOutputSync)
            {
                logText = liveTestOutput.ToString();
            }

            var testsExecuted = DotNetTestOutputParser.LooksLikeTestsExecuted(logText);
            var testSummary = DotNetTestOutputParser.TryParseSummary(logText);
            var summaryLine = testSummary is not null
                ? DotNetTestOutputParser.FormatSummaryLine(testSummary)
                : DescribeMissingTestSummary(logText, testsExecuted);
            var finishBanner = BuildMonitorLogBanner.FormatTestFinished(
                testNumber,
                testsExecuted ? exitCode : 1,
                summaryLine,
                wallDuration);
            lock (liveOutputSync)
            {
                liveTestOutput.AppendLine(finishBanner);
            }

            Interlocked.Increment(ref liveTestOutputRevision);

            lock (liveOutputSync)
            {
                logText = liveTestOutput.ToString();
            }

            var parsed = BuildLogParser.ParseErrors(logText);
            var effectiveExitCode = testsExecuted ? exitCode : 1;
            await logStore.SaveAsync(
                projectSettings.Id,
                BuildLogKind.Test,
                string.Join(" && ", commandLines),
                effectiveExitCode,
                startedAtUtc,
                logText,
                cancellationToken);

            if (effectiveExitCode == 0)
            {
                buildErrorCount = preservedBuildErrors;
                buildWarningCount = preservedBuildWarnings;
                SetState(ProjectLifecycleState.TestOk);
            }
            else
            {
                buildErrorCount = Math.Max(parsed.ErrorCount, testsExecuted ? 0 : 1);
                buildWarningCount = BuildLogParser.ParseWarningCount(logText);
                lastErrorPreview = parsed.ErrorLines.FirstOrDefault()
                    ?? summaryLine
                    ?? "No tests were executed";
                SetState(ProjectLifecycleState.TestFailed);
            }
        }
        finally
        {
            Interlocked.Exchange(ref testInProgress, 0);
            fileChangeBuildCooldownUntil = DateTimeOffset.UtcNow.AddSeconds(10);
            fileWatcher?.Resume();

            if (stoppedAppForTests
                && wasRunProcessActive
                && Local.RunOptions.RunMode != ProjectRunMode.None)
            {
                _ = RestartRunProcessAfterTestsAsync();
            }

            HealthCoalesceRequested?.Invoke(true);
        }
    }

    private sealed record TestTargetRunResult(CliRunResult Result, bool StoppedApp);

    private async Task RestartRunProcessAfterTestsAsync()
    {
        await Task.Delay(2500);

        if (Volatile.Read(ref testInProgress) != 0 || Volatile.Read(ref buildInProgress) != 0)
        {
            return;
        }

        StartRunProcess(skipEmbeddedBuild: true);
    }

    private async Task<TestTargetRunResult> RunTestTargetWithRetryAsync(
        string target,
        bool wasRunProcessActive,
        bool releaseLocksSetting,
        CancellationToken cancellationToken)
    {
        var stoppedApp = false;
        CliRunResult result;
        var usedNoBuild = false;

        if (TestRunPlanner.RequiresFullBuildFromStart(lastBuildExitCode))
        {
            stoppedApp = await StopAppForTestBuildIfNeededAsync(
                wasRunProcessActive,
                "stopping run/watch to rebuild before tests",
                cancellationToken);
            await ReleaseLocksForTestBuildIfNeededAsync(releaseLocksSetting, stoppedApp, cancellationToken);
            result = await RunTestAttemptAsync(BuildTestArgs(target, noBuild: false), cancellationToken);
        }
        else
        {
            AppendTestNote("running tests while app stays up (--no-build)");
            usedNoBuild = true;
            result = await RunTestAttemptAsync(BuildTestArgs(target, noBuild: true), cancellationToken);

            if (!DotNetTestOutputParser.LooksLikeTestsExecuted(result.Output)
                && DotNetTestOutputParser.LooksLikeNeedsFullBuildBeforeTest(result.Output))
            {
                usedNoBuild = false;
                stoppedApp = await StopAppForTestBuildIfNeededAsync(
                    wasRunProcessActive,
                    "test assemblies stale — stopping app briefly to rebuild",
                    cancellationToken);
                await ReleaseLocksForTestBuildIfNeededAsync(releaseLocksSetting, stoppedApp, cancellationToken);
                result = await RunTestAttemptAsync(BuildTestArgs(target, noBuild: false), cancellationToken);
            }
        }

        var shouldReleaseLocks = TestRunPlanner.ShouldReleaseLocksForTestBuild(releaseLocksSetting, stoppedApp);
        var finalResult = await RetryTestOnLockErrorAsync(
            result,
            target,
            usedNoBuild,
            shouldReleaseLocks,
            wasRunProcessActive,
            cancellationToken);

        return new TestTargetRunResult(finalResult.Result, finalResult.StoppedApp || stoppedApp);
    }

    private async Task<bool> StopAppForTestBuildIfNeededAsync(
        bool wasRunProcessActive,
        string note,
        CancellationToken cancellationToken)
    {
        if (!wasRunProcessActive || runProcess?.IsRunning != true)
        {
            return false;
        }

        AppendTestNote(note);
        await StopRunProcessAsync(cancellationToken);
        return true;
    }

    private async Task ReleaseLocksForTestBuildIfNeededAsync(
        bool releaseLocksSetting,
        bool stoppedApp,
        CancellationToken cancellationToken)
    {
        if (!TestRunPlanner.ShouldReleaseLocksForTestBuild(releaseLocksSetting, stoppedApp))
        {
            return;
        }

        await ReleaseOutputLocksAsync(cancellationToken);
    }

    private async Task<TestTargetRunResult> RetryTestOnLockErrorAsync(
        CliRunResult result,
        string target,
        bool noBuild,
        bool shouldReleaseLocks,
        bool wasRunProcessActive,
        CancellationToken cancellationToken)
    {
        if (result.ExitCode == 0 || !BuildLogParser.IsOutputLockError(result.Output))
        {
            return new TestTargetRunResult(result, false);
        }

        var stoppedApp = false;
        if (shouldReleaseLocks || wasRunProcessActive)
        {
            stoppedApp = await StopAppForTestBuildIfNeededAsync(
                wasRunProcessActive,
                "output locked — stopping app before retrying tests",
                cancellationToken);
            AppendTestNote("output locked — releasing and retrying tests");
            await ReleaseOutputLocksAsync(cancellationToken);
            await Task.Delay(1000, cancellationToken);
            result = await RunTestAttemptAsync(BuildTestArgs(target, noBuild: false), cancellationToken);
        }

        return new TestTargetRunResult(result, stoppedApp);
    }

    private async Task<CliRunResult> RunTestAttemptAsync(
        List<string> args,
        CancellationToken cancellationToken) =>
        await cliRunner.RunAsync(
            Local.RootFolder,
            args,
            cancellationToken,
            OnTestOutputLine);

    private void AppendTestNote(string note)
    {
        lock (liveOutputSync)
        {
            liveTestOutput.AppendLine($"[BuildMonitor] {note}");
            liveTestOutput.AppendLine(string.Empty);
        }

        Interlocked.Increment(ref liveTestOutputRevision);
    }

    private string WriteTestStartBanner(string reason, TestTargetResolution resolution)
    {
        var banner = BuildMonitorLogBanner.FormatTest(Interlocked.Increment(ref testNumber), reason);
        lock (liveOutputSync)
        {
            liveTestOutput.AppendLine(banner);
            liveTestOutput.AppendLine($"[BuildMonitor] {resolution.DiscoveryNote}");
            if (resolution.Targets.Count == 1)
            {
                var tryNoBuild = lastBuildExitCode == 0;
                liveTestOutput.AppendLine(
                    $"dotnet {string.Join(' ', BuildTestArgs(resolution.Targets[0], tryNoBuild))}"
                    + (tryNoBuild ? " (app stays up; brief stop only if assemblies are stale)" : string.Empty));
            }

            liveTestOutput.AppendLine(string.Empty);
        }

        Interlocked.Increment(ref liveTestOutputRevision);
        return banner;
    }

    private void WriteTestStartBanner(string reason, IReadOnlyList<string> args, string note)
    {
        var banner = BuildMonitorLogBanner.FormatTest(Interlocked.Increment(ref testNumber), reason);
        lock (liveOutputSync)
        {
            liveTestOutput.AppendLine(banner);
            liveTestOutput.AppendLine($"[BuildMonitor] {note}");
            if (args.Count > 0)
            {
                liveTestOutput.AppendLine($"dotnet {string.Join(' ', args)}");
            }

            liveTestOutput.AppendLine(string.Empty);
        }

        Interlocked.Increment(ref liveTestOutputRevision);
    }

    private void AppendTestSectionHeader(int index, int total, string target)
    {
        lock (liveOutputSync)
        {
            liveTestOutput.AppendLine($"[BuildMonitor] --- Test target {index}/{total}: {target} ---");
            liveTestOutput.AppendLine($"dotnet {string.Join(' ', BuildTestArgs(target, lastBuildExitCode == 0))}");
            liveTestOutput.AppendLine(string.Empty);
        }

        Interlocked.Increment(ref liveTestOutputRevision);
    }

    private static string? DescribeMissingTestSummary(string logText, bool testsExecuted)
    {
        if (testsExecuted)
        {
            return null;
        }

        if (BuildLogParser.IsOutputLockError(logText))
        {
            return "build failed — app executable is locked; enable Stop processes locking build output in settings";
        }

        if (logText.Contains("No test is available", StringComparison.OrdinalIgnoreCase)
            || logText.Contains("No tests found", StringComparison.OrdinalIgnoreCase))
        {
            return "no tests discovered in target — set Test project / solution in settings";
        }

        if (DotNetTestOutputParser.LooksLikeRestoreOrBuildOnly(logText))
        {
            return "no tests executed (build did not reach test host) — check build errors above";
        }

        return "no tests executed";
    }

    private void OnTestOutputLine(string line)
    {
        lock (liveOutputSync)
        {
            liveTestOutput.AppendLine(line);
        }

        Interlocked.Increment(ref liveTestOutputRevision);
        HeartbeatProjectWorker("test-output");
        RequestHealthCoalesce(immediate: false);
    }

    private List<string> BuildTestArgs(string testTargetPath, bool noBuild = false)
    {
        var args = new List<string>
        {
            "test",
            testTargetPath,
            "--verbosity",
            "normal",
            "--logger",
            "console;verbosity=detailed"
        };

        if (noBuild)
        {
            args.Add("--no-build");
        }

        if (!string.IsNullOrWhiteSpace(shipCheckConfiguration))
        {
            args.Add("-c");
            args.Add(shipCheckConfiguration);
        }

        if (!string.IsNullOrWhiteSpace(shipCheckFilter))
        {
            args.Add("--filter");
            args.Add(shipCheckFilter);
        }

        AppendExtraArgs(args);
        return args;
    }
}
