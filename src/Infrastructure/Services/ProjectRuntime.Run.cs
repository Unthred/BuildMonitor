using System.Text;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Infrastructure.Services;

internal sealed partial class ProjectRuntime
{
    /// <summary>
    /// After the first profile URL answers, wait this long for the preferred scheme (HTTPS)
    /// before marking site-ready on a fallback (HTTP). Measured from first open — not process start —
    /// so long builds do not burn the grace window.
    /// </summary>
    private static readonly TimeSpan ListenUrlPreferredSchemeGrace = TimeSpan.FromSeconds(30);

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
        pendingListenUrl = LocalPortProbe.SelectPreferredProfileUrl(
                candidateListenUrls,
                definition.PreferredSiteUrlScheme)
            ?? candidateListenUrls.FirstOrDefault();
        listenUrlReady = false;
        listenUrlNotified = false;
        listenUrlFirstOpenUtc = null;
        runOutputSaveRevision = 0;
        StartListenUrlPolling();
        StartRunLogSaveTimer();

        runProcess.Start(
            definition.RootFolder,
            args,
            psi =>
            {
                // When --launch-profile is on the command line, dotnet applies launchSettings itself.
                if (string.IsNullOrWhiteSpace(ResolveEffectiveLaunchProfile()))
                {
                    LaunchProfileEnvironmentApplier.ApplyTo(
                        psi,
                        definition.RootFolder,
                        definition.ProjectFile,
                        definition.LaunchProfile);
                }

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
        listenUrlFirstOpenUtc = null;
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
        listenUrlFirstOpenUtc = null;

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
        var args = new List<string> { "run", "--project", ResolveProjectFileArg() };
        AppendLaunchProfileSwitch(args);
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

        args.AddRange(["run", "--project", ResolveProjectFileArg()]);
        AppendLaunchProfileSwitch(args);
        if (skipEmbeddedBuild)
        {
            args.Add("--no-build");
        }

        AppendExtraArgs(args);
        return args;
    }

    private void AppendLaunchProfileSwitch(List<string> args)
    {
        var profile = ResolveEffectiveLaunchProfile();
        if (!string.IsNullOrWhiteSpace(profile))
        {
            args.Add("--launch-profile");
            args.Add(profile);
            return;
        }

        args.Add("--no-launch-profile");
    }

    private string? ResolveEffectiveLaunchProfile() =>
        LaunchProfileEnvironmentApplier.ResolveEffectiveLaunchProfile(
            definition.RootFolder,
            definition.ProjectFile,
            definition.LaunchProfile);

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

        var preference = definition.PreferredSiteUrlScheme;

        // While awaiting readiness, always surface the preferred profile URL (HTTPS), not a
        // transient HTTP listen line. When ready, still re-canonicalise with preference so an
        // upgraded HTTPS endpoint wins over a stale HTTP pending value.
        if (!listenUrlReady)
        {
            return LocalPortProbe.ResolveCanonicalUserFacingUrl(
                null,
                candidateListenUrls,
                preference)
                ?? LocalPortProbe.ResolveCanonicalUserFacingUrl(
                    pendingListenUrl,
                    candidateListenUrls,
                    preference);
        }

        return LocalPortProbe.ResolveCanonicalUserFacingUrl(
            pendingListenUrl,
            candidateListenUrls,
            preference);
    }

    private void RefreshListenUrlReady()
    {
        if (runProcess?.IsRunning != true
            || state is not (ProjectLifecycleState.Running or ProjectLifecycleState.Watching))
        {
            listenUrlReady = false;
            return;
        }

        var preference = definition.PreferredSiteUrlScheme;
        var urlsToProbe = candidateListenUrls.Count > 0
            ? candidateListenUrls
            : string.IsNullOrWhiteSpace(pendingListenUrl) ? [] : new[] { pendingListenUrl };

        var openUrls = urlsToProbe.Where(LocalPortProbe.IsHttpEndpointOpen).ToList();
        if (openUrls.Count == 0)
        {
            listenUrlReady = false;
            return;
        }

        listenUrlFirstOpenUtc ??= DateTimeOffset.UtcNow;
        var graceExpired = DateTimeOffset.UtcNow - listenUrlFirstOpenUtc.Value >= ListenUrlPreferredSchemeGrace;

        var preferred = LocalPortProbe.SelectPreferredProfileUrl(candidateListenUrls, preference);
        var preferredOpen = preferred is not null
            && openUrls.Any(open => LocalPortProbe.SameListenEndpoint(open, preferred));

        // Preferred scheme is up — always lock onto it (including upgrades from HTTP).
        if (preferredOpen)
        {
            var preferredCanonical = LocalPortProbe.ResolveCanonicalUserFacingUrl(
                preferred,
                candidateListenUrls,
                preference) ?? preferred;
            MarkListenUrlReady(preferredCanonical!);
            return;
        }

        if (LocalPortProbe.ShouldWaitForPreferredScheme(
                openUrls,
                candidateListenUrls,
                preference,
                graceExpired))
        {
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                pendingListenUrl = preferred;
            }

            return;
        }

        var canonical = LocalPortProbe.ResolveCanonicalUserFacingUrlFromOpenEndpoints(
            openUrls,
            candidateListenUrls,
            preference);
        if (string.IsNullOrWhiteSpace(canonical))
        {
            listenUrlReady = false;
            return;
        }

        MarkListenUrlReady(canonical);
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
        RefreshListenUrlReady();

        // Stop only once the preferred profile URL is the ready URL (or there is no preference).
        // Keep polling after an HTTP fallback so late HTTPS can still upgrade.
        if (!listenUrlReady)
        {
            return;
        }

        var preference = definition.PreferredSiteUrlScheme;
        var preferred = LocalPortProbe.SelectPreferredProfileUrl(candidateListenUrls, preference);
        if (preferred is null
            || (!string.IsNullOrWhiteSpace(pendingListenUrl)
                && LocalPortProbe.SameListenEndpoint(pendingListenUrl, preferred)))
        {
            StopListenUrlPolling();
        }
    }

    private void MarkListenUrlReady(string url)
    {
        var preference = definition.PreferredSiteUrlScheme;
        if (listenUrlReady
            && !LocalPortProbe.IsBetterCanonicalUrl(
                url,
                pendingListenUrl,
                candidateListenUrls,
                preference))
        {
            return;
        }

        var upgraded = listenUrlReady
            && !string.IsNullOrWhiteSpace(pendingListenUrl)
            && !LocalPortProbe.SameListenEndpoint(url, pendingListenUrl!);

        pendingListenUrl = url;
        if (!listenUrlReady)
        {
            listenUrlReady = true;
            if (runProcess?.IsRunning == true)
            {
                runErrorCount = 0;
            }
        }

        RefreshHealth();
        NotifyProgressChanged(force: true);

        if (listenUrlNotified && !upgraded)
        {
            return;
        }

        if (!listenUrlNotified)
        {
            listenUrlNotified = true;
            notifyUser?.Invoke(
                definition.Id,
                $"App running — {definition.DisplayName}",
                $"Open {url}",
                UserNotificationKind.Info,
                UserNotificationCategory.Info);
        }
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
