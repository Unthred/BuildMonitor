using System.Text;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Infrastructure.Services;

internal sealed partial class ProjectRuntime
{
    private void StartRunProcess(bool skipEmbeddedBuild = false)
    {
        SetProjectCurrentAction(skipEmbeddedBuild
            ? "Starting app (dotnet run --no-build)"
            : "Starting app (dotnet run)");
        StopRunProcess();
        WarnIfRiskyBaseOutputPath();
        runErrorCount = 0;
        runWarningCount = 0;

        runProcessGeneration++;
        var generation = runProcessGeneration;

        runProcess = new SupervisedProcess(definition.Id);
        runProcess.OutputLineReceived += OnRunProcessOutputLine;

        runProcessExitedHandler = (_, exitCode) =>
        {
            if (generation != runProcessGeneration)
            {
                return;
            }

            OnRunProcessExited(exitCode);
        };
        runProcess.Exited += runProcessExitedHandler;

        var args = UsesDotNetWatchProcess()
            ? BuildWatchArgs(skipEmbeddedBuild)
            : BuildRunArgs(skipEmbeddedBuild);

        candidateListenUrls = LaunchProfileEnvironmentApplier.ResolveListenUrls(
            definition.RootFolder,
            definition.ProjectFile,
            definition.LaunchProfile);
        pendingListenUrl = candidateListenUrls.FirstOrDefault();
        listenUrlReady = false;
        listenUrlNotified = false;
        runOutputSaveRevision = 0;
        StartListenUrlPolling();
        StartRunLogSaveTimer();

        runProcess.Start(
            definition.RootFolder,
            args,
            psi =>
            {
                var effectiveProfile = LaunchProfileEnvironmentApplier.ResolveEffectiveLaunchProfile(
                    definition.RootFolder,
                    definition.ProjectFile,
                    definition.LaunchProfile);
                LaunchProfileEnvironmentApplier.ApplyTo(
                    psi,
                    definition.RootFolder,
                    definition.ProjectFile,
                    effectiveProfile);

                // BuildMonitor shows site-ready in the tray panel; avoid launchSettings launchBrowser pop-ups.
                psi.Environment["DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER"] = "1";

                if (UsesDotNetWatchProcess()
                    && !definition.RunOptions.AutoRestartOnWatchChanges)
                {
                    psi.Environment["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"] = "0";
                }
            });

        NotifyProgressChanged(force: true);

        SetState(definition.RunOptions.RunMode == ProjectRunMode.Watch
            || UsesCoalescedWatchRebuilds()
            ? ProjectLifecycleState.Watching
            : ProjectLifecycleState.Running);
    }

    private void OnRunProcessExited(int exitCode)
    {
        var exitedProcess = runProcess;
        if (exitedProcess is null)
        {
            return;
        }

        StopListenUrlPolling();
        StopRunLogSaveTimer();
        SaveRunOutputIfChanged(force: true);
        listenUrlReady = false;
        listenUrlNotified = false;
        lastExitCode = exitCode;
        var runOutput = exitedProcess.Output;
        if (exitCode == 0)
        {
            runErrorCount = 0;
            runWarningCount = DotNetRunOutputParser.ParseWarningCount(runOutput);
        }
        else
        {
            runErrorCount = DotNetRunOutputParser.ParseErrorCount(runOutput);
            runWarningCount = DotNetRunOutputParser.ParseWarningCount(runOutput);
            if (runErrorCount == 0)
            {
                runErrorCount = 1;
            }
        }

        if (exitCode != 0 && definition.RunOptions.RestartOnCrash && restartCount < definition.RunOptions.MaxRestartRetries)
        {
            restartCount++;
            SetState(ProjectLifecycleState.Crashed);
            StartRunProcess(skipEmbeddedBuild: true);
            return;
        }

        if (exitCode != 0)
        {
            _ = logStore.SaveAsync(
                definition.Id,
                BuildLogKind.Run,
                exitedProcess.CommandLine,
                exitCode,
                DateTimeOffset.UtcNow,
                exitedProcess.Output,
                CancellationToken.None);
            SetState(ProjectLifecycleState.Crashed);
        }
        else
        {
            SetState(ProjectLifecycleState.Idle);
        }
    }

    private void StopRunProcess()
    {
        StopListenUrlPolling();
        StopRunLogSaveTimer();
        listenUrlReady = false;
        listenUrlNotified = false;

        if (runProcess is null)
        {
            return;
        }

        SaveRunOutputIfChanged(force: true);
        runProcessGeneration++;
        DetachRunProcessHandlers();
        runProcess.Stop();
        runProcess = null;
    }

    private async Task StopRunProcessAsync(CancellationToken cancellationToken)
    {
        if (runProcess is null)
        {
            listenUrlReady = false;
            listenUrlNotified = false;
            return;
        }

        listenUrlReady = false;
        listenUrlNotified = false;
        runProcessGeneration++;
        DetachRunProcessHandlers();
        await runProcess.StopGracefullyAsync(cancellationToken);
        runProcess = null;
    }

    private void DetachRunProcessHandlers()
    {
        if (runProcess is null)
        {
            return;
        }

        runProcess.OutputLineReceived -= OnRunProcessOutputLine;
        if (runProcessExitedHandler is not null)
        {
            runProcess.Exited -= runProcessExitedHandler;
            runProcessExitedHandler = null;
        }
    }

    private List<string> BuildRunArgs(bool skipEmbeddedBuild = false)
    {
        var args = new List<string> { "run", "--project", ResolveProjectFileArg(), "--no-launch-profile" };
        if (skipEmbeddedBuild)
        {
            args.Add("--no-build");
        }

        AppendExtraArgs(args);
        return args;
    }

    private List<string> BuildWatchArgs(bool skipEmbeddedBuild = false)
    {
        var args = new List<string> { "watch" };
        if (definition.RunOptions.AutoRestartOnWatchChanges)
        {
            // Tray host has no stdin for restart prompts — auto-restart when enabled per project.
            args.Add("--non-interactive");
        }

        args.AddRange(["run", "--project", ResolveProjectFileArg(), "--no-launch-profile"]);
        if (skipEmbeddedBuild)
        {
            args.Add("--no-build");
        }

        AppendExtraArgs(args);
        return args;
    }

    private void AppendExtraArgs(List<string> args)
    {
        if (string.IsNullOrWhiteSpace(definition.ExtraDotNetArgs))
        {
            return;
        }

        args.AddRange(definition.ExtraDotNetArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private string? ResolveDisplayListenUrl()
    {
        if (definition.RunOptions.RunMode == ProjectRunMode.None)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(pendingListenUrl))
        {
            return LocalPortProbe.NormalizeDisplayUrl(
                LocalPortProbe.PreferProfileDisplayUrl(pendingListenUrl, candidateListenUrls));
        }

        if (candidateListenUrls.Count > 0)
        {
            return LocalPortProbe.NormalizeDisplayUrl(candidateListenUrls[0]);
        }

        var profileUrl = LaunchProfileEnvironmentApplier.ResolvePrimaryListenUrl(
            definition.RootFolder,
            definition.ProjectFile,
            definition.LaunchProfile);
        return string.IsNullOrWhiteSpace(profileUrl)
            ? null
            : LocalPortProbe.NormalizeDisplayUrl(profileUrl);
    }

    private void RefreshListenUrlReady()
    {
        if (runProcess?.IsRunning != true
            || state is not (ProjectLifecycleState.Running or ProjectLifecycleState.Watching))
        {
            listenUrlReady = false;
            return;
        }

        var urlsToProbe = candidateListenUrls.Count > 0
            ? candidateListenUrls
            : string.IsNullOrWhiteSpace(pendingListenUrl) ? [] : new[] { pendingListenUrl };

        foreach (var url in urlsToProbe)
        {
            if (LocalPortProbe.IsHttpEndpointOpen(url))
            {
                MarkListenUrlReady(url);
                return;
            }
        }

        listenUrlReady = false;
    }

    private void StartListenUrlPolling()
    {
        StopListenUrlPolling();
        listenUrlPollTimer = new Timer(
            _ => PollListenUrl(),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    private void StopListenUrlPolling()
    {
        listenUrlPollTimer?.Dispose();
        listenUrlPollTimer = null;
    }

    private void PollListenUrl()
    {
        if (listenUrlReady)
        {
            return;
        }

        RefreshListenUrlReady();
    }

    private void MarkListenUrlReady(string url)
    {
        pendingListenUrl = url;
        if (listenUrlReady)
        {
            return;
        }

        listenUrlReady = true;
        StopListenUrlPolling();
        if (runProcess?.IsRunning == true)
        {
            runErrorCount = 0;
        }

        RefreshHealth();
        NotifyProgressChanged(force: true);

        if (listenUrlNotified)
        {
            return;
        }

        listenUrlNotified = true;
        var openUrl = LocalPortProbe.NormalizeBrowserUrl(url);
        notifyUser?.Invoke(
            definition.Id,
            $"App running — {definition.DisplayName}",
            $"Open {openUrl}",
            UserNotificationKind.Info,
            UserNotificationCategory.Info);
    }

    public Task StopAsync()
    {
        fileWatcher?.Dispose();
        fileWatcher = null;
        StopListenUrlPolling();
        StopRunProcess();
        buildProgressTracker = null;
        progressSteps = [];
        SetState(ProjectLifecycleState.Idle);
        return Task.CompletedTask;
    }
}
