using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Git;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Infrastructure.Services;

public sealed partial class ProjectOrchestrator
{
    private IGitWorktreeIdentityReader gitWorktreeIdentityReader = new GitWorktreeIdentityReader();
    private Func<string, CancellationToken, Task>? derivedWorktreeHostStarter;

    /// <summary>Test seam for Git identity checks during derived-worktree register.</summary>
    internal void SetGitWorktreeIdentityReader(IGitWorktreeIdentityReader reader) =>
        gitWorktreeIdentityReader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <summary>Test seam to observe or skip cold-start after derived register.</summary>
    internal void SetDerivedWorktreeHostStarter(Func<string, CancellationToken, Task>? starter) =>
        derivedWorktreeHostStarter = starter;

    public async Task<ControlPlaneRegisterWorktreeResult> RegisterDerivedWorktreeAsync(
        ControlPlaneRegisterWorktreeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ParentProjectId))
        {
            return ControlPlaneRegisterWorktreeResult.Fail("parentProjectId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.WorktreePath))
        {
            return ControlPlaneRegisterWorktreeResult.Fail("worktreePath is required.");
        }

        string worktreePath;
        try
        {
            worktreePath = VerificationProviderPath.Normalize(request.WorktreePath);
        }
        catch (Exception ex)
        {
            return ControlPlaneRegisterWorktreeResult.Fail($"worktreePath is invalid: {ex.Message}");
        }

        if (!Directory.Exists(worktreePath))
        {
            return ControlPlaneRegisterWorktreeResult.Fail(
                $"worktreePath does not exist: {worktreePath}");
        }

        MonitoredProjectSettings? parent;
        MonitoredProjectSettings? existingAtPath;
        AppSettings snapshot;
        lock (sync)
        {
            snapshot = settings;
            parent = settings.Projects.FirstOrDefault(p =>
                string.Equals(p.Id, request.ParentProjectId, StringComparison.OrdinalIgnoreCase));
            existingAtPath = settings.Projects.FirstOrDefault(p =>
                p.Local is not null
                && VerificationProviderPath.EqualsExact(p.Local.RootFolder, worktreePath));
        }

        if (parent is null)
        {
            return ControlPlaneRegisterWorktreeResult.Fail(
                $"Unknown parentProjectId '{request.ParentProjectId}'.");
        }

        if (parent.Local is null)
        {
            return ControlPlaneRegisterWorktreeResult.Fail(
                $"Parent project '{parent.Id}' has no Local attachment to derive from.");
        }

        if (existingAtPath is not null)
        {
            var derivedFrom = existingAtPath.Local?.DerivedFromProjectId;
            var alreadyFromParent = string.Equals(
                derivedFrom,
                parent.Id,
                StringComparison.OrdinalIgnoreCase);
            if (alreadyFromParent
                || VerificationProviderPath.EqualsExact(existingAtPath.Local!.RootFolder, worktreePath))
            {
                return ControlPlaneRegisterWorktreeResult.SuccessAlreadyRegistered(
                    existingAtPath.Id,
                    existingAtPath.DisplayName,
                    existingAtPath.Local!.RootFolder,
                    string.IsNullOrWhiteSpace(existingAtPath.Local.ApplicationUrl)
                        ? null
                        : existingAtPath.Local.ApplicationUrl,
                    parent.Id);
            }

            return ControlPlaneRegisterWorktreeResult.Fail(
                $"worktreePath is already registered as project '{existingAtPath.Id}'.");
        }

        var parentIdentity = await gitWorktreeIdentityReader
            .TryReadAsync(parent.Local.RootFolder, cancellationToken)
            .ConfigureAwait(false);
        if (parentIdentity is not { IsInsideWorkTree: true }
            || string.IsNullOrWhiteSpace(parentIdentity.CommonDirAbsolute))
        {
            return ControlPlaneRegisterWorktreeResult.Fail(
                "Parent project root is not a readable Git worktree.");
        }

        var worktreeIdentity = await gitWorktreeIdentityReader
            .TryReadAsync(worktreePath, cancellationToken)
            .ConfigureAwait(false);
        if (worktreeIdentity is null)
        {
            return ControlPlaneRegisterWorktreeResult.Fail(
                "Could not read Git identity for worktreePath.");
        }

        if (!worktreeIdentity.IsInsideWorkTree)
        {
            return ControlPlaneRegisterWorktreeResult.Fail(
                "worktreePath is not inside a Git worktree.");
        }

        if (!GitWorktreeIdentity.SameRepository(
                parentIdentity.CommonDirAbsolute,
                worktreeIdentity.CommonDirAbsolute))
        {
            return ControlPlaneRegisterWorktreeResult.Fail(
                "worktreePath belongs to a different Git repository than the parent project.");
        }

        string? allocatedUrl;
        try
        {
            allocatedUrl = AllocateIsolatedApplicationUrl(parent, worktreePath, snapshot);
        }
        catch (InvalidOperationException ex)
        {
            return ControlPlaneRegisterWorktreeResult.Fail(ex.Message);
        }

        var newId = Guid.NewGuid().ToString("N");
        MonitoredProjectSettings derived;
        try
        {
            derived = DerivedWorktreeProjectFactory.Create(parent, worktreePath, newId, allocatedUrl);
        }
        catch (Exception ex)
        {
            return ControlPlaneRegisterWorktreeResult.Fail(ex.Message);
        }

        lock (sync)
        {
            // Re-check under lock for races.
            if (settings.Projects.Any(p =>
                    p.Local is not null
                    && VerificationProviderPath.EqualsExact(p.Local.RootFolder, worktreePath)))
            {
                var again = settings.Projects.First(p =>
                    p.Local is not null
                    && VerificationProviderPath.EqualsExact(p.Local.RootFolder, worktreePath));
                return ControlPlaneRegisterWorktreeResult.SuccessAlreadyRegistered(
                    again.Id,
                    again.DisplayName,
                    again.Local!.RootFolder,
                    string.IsNullOrWhiteSpace(again.Local.ApplicationUrl) ? null : again.Local.ApplicationUrl,
                    parent.Id);
            }

            settings.Projects.Add(derived);
            settings.SchemaVersion = Math.Max(settings.SchemaVersion, SettingsSchemaV26.Version);
            snapshot = settings;
        }

        ApplySettings(snapshot);
        settingsPersistRequested?.Invoke(GetSettingsSnapshot());
        healthCoalescer.Request(immediate: true);

        if (derived.Local.StartOnLaunch
            && derived.Local.RunOptions.RunMode != ProjectRunMode.None)
        {
            if (derivedWorktreeHostStarter is not null)
            {
                await derivedWorktreeHostStarter(derived.Id, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                ProjectRuntime? runtime;
                lock (sync)
                {
                    runtimes.TryGetValue(derived.Id, out runtime);
                }

                if (runtime is not null)
                {
                    try
                    {
                        await runtime.StartAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        RaiseUserNotification(
                            runtime.ProjectId,
                            $"Failed to start {runtime.DisplayName}",
                            ExceptionDetailFormatter.Format(ex),
                            UserNotificationKind.Error,
                            UserNotificationCategory.Error);
                    }
                }
            }
        }

        return ControlPlaneRegisterWorktreeResult.SuccessCreated(
            derived.Id,
            derived.DisplayName,
            derived.Local!.RootFolder,
            string.IsNullOrWhiteSpace(derived.Local.ApplicationUrl) ? null : derived.Local.ApplicationUrl,
            parent.Id);
    }

    public async Task<ControlPlaneUnregisterWorktreeResult> UnregisterDerivedWorktreeAsync(
        ControlPlaneUnregisterWorktreeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProjectId))
        {
            return ControlPlaneUnregisterWorktreeResult.Fail("projectId is required.");
        }

        MonitoredProjectSettings? project;
        lock (sync)
        {
            project = settings.Projects.FirstOrDefault(p =>
                string.Equals(p.Id, request.ProjectId, StringComparison.OrdinalIgnoreCase));
        }

        if (project is null)
        {
            return ControlPlaneUnregisterWorktreeResult.SuccessAlreadyRemoved(request.ProjectId);
        }

        if (string.IsNullOrWhiteSpace(project.Local?.DerivedFromProjectId))
        {
            return ControlPlaneUnregisterWorktreeResult.Fail(
                $"Project '{project.Id}' is not a derived worktree registration; refusing to remove.",
                project.Id);
        }

        if (!string.IsNullOrWhiteSpace(request.WorktreePath))
        {
            string confirmPath;
            try
            {
                confirmPath = VerificationProviderPath.Normalize(request.WorktreePath);
            }
            catch (Exception ex)
            {
                return ControlPlaneUnregisterWorktreeResult.Fail(
                    $"worktreePath is invalid: {ex.Message}",
                    project.Id);
            }

            if (project.Local is null
                || !VerificationProviderPath.EqualsExact(project.Local.RootFolder, confirmPath))
            {
                return ControlPlaneUnregisterWorktreeResult.Fail(
                    "worktreePath does not match the registered derived project rootFolder.",
                    project.Id);
            }
        }

        ProjectRuntime? runtime;
        lock (sync)
        {
            runtimes.TryGetValue(project.Id, out runtime);
        }

        if (runtime is not null && runtime.HasExclusiveControlPlaneOperation())
        {
            return ControlPlaneUnregisterWorktreeResult.FailBusy(
                project.Id,
                "A control-plane rebuild/tests/ship-check is still running for this project.");
        }

        if (runtime is not null)
        {
            await StopControlPlaneRunAsync(project.Id, cancellationToken).ConfigureAwait(false);
        }

        // Re-check busy after stop — a concurrent /run/* may have started.
        lock (sync)
        {
            runtimes.TryGetValue(project.Id, out runtime);
        }

        if (runtime is not null && runtime.HasExclusiveControlPlaneOperation())
        {
            return ControlPlaneUnregisterWorktreeResult.FailBusy(
                project.Id,
                "A control-plane rebuild/tests/ship-check is still running for this project.");
        }

        AppSettings snapshot;
        lock (sync)
        {
            var removed = settings.Projects.RemoveAll(p =>
                string.Equals(p.Id, project.Id, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(p.Local?.DerivedFromProjectId));
            if (removed == 0)
            {
                return ControlPlaneUnregisterWorktreeResult.SuccessAlreadyRemoved(project.Id);
            }

            snapshot = settings;
        }

        await StopProjectAsync(project.Id).ConfigureAwait(false);
        ApplySettings(snapshot);
        settingsPersistRequested?.Invoke(GetSettingsSnapshot());
        healthCoalescer.Request(immediate: true);

        return ControlPlaneUnregisterWorktreeResult.SuccessRemoved(project.Id);
    }

    private string? AllocateIsolatedApplicationUrl(
        MonitoredProjectSettings parent,
        string worktreePath,
        AppSettings snapshot)
    {
        // Capture parent context against the *worktree* root so launchSettings paths resolve
        // under the new tree (same relative project file).
        var probeLocal = new LocalProjectAttachment
        {
            RootFolder = worktreePath,
            ProjectFile = DerivedWorktreeProjectFactory.RemapPathUnderRoot(
                parent.Local!.ProjectFile,
                parent.Local.RootFolder,
                worktreePath) ?? parent.Local.ProjectFile,
            LaunchProfile = parent.Local.LaunchProfile,
            ApplicationUrl = string.Empty
        };
        var probeProject = new MonitoredProjectSettings
        {
            Id = "probe",
            DisplayName = "probe",
            Local = probeLocal
        };

        IReadOnlyList<string> profileUrls;
        try
        {
            var context = ProjectRunContextFactory.Capture(probeProject);
            profileUrls = context.ProfileListenUrls;
        }
        catch
        {
            profileUrls = [];
        }

        var peers = snapshot.Projects
            .Where(p => p.Local is not null)
            .Select(p =>
            {
                var urls = ProjectPortIsolation.SplitApplicationUrl(p.Local!.ApplicationUrl);
                if (urls.Count == 0 && string.Equals(p.Id, parent.Id, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var parentContext = ProjectRunContextFactory.Capture(parent);
                        urls = string.IsNullOrWhiteSpace(parent.Local!.ApplicationUrl)
                            ? parentContext.ProfileListenUrls
                            : ProjectPortIsolation.SplitApplicationUrl(parent.Local.ApplicationUrl);
                        if (urls.Count == 0)
                        {
                            urls = parentContext.ProfileListenUrls;
                        }
                    }
                    catch
                    {
                        urls = [];
                    }
                }

                return new PeerProjectListenInfo(
                    p.Id,
                    p.DisplayName,
                    p.Local!.RootFolder,
                    urls,
                    IsRunProcessActive: true);
            })
            .Where(p => p.ListenUrls.Count > 0)
            .ToList();

        // Always include live runtime listen URLs when available.
        lock (sync)
        {
            foreach (var runtime in runtimes.Values)
            {
                var owned = runtime.GetOwnedListenUrls();
                if (owned.Count == 0)
                {
                    continue;
                }

                var existing = peers.FindIndex(p =>
                    string.Equals(p.ProjectId, runtime.ProjectId, StringComparison.OrdinalIgnoreCase));
                var peer = new PeerProjectListenInfo(
                    runtime.ProjectId,
                    runtime.DisplayName,
                    runtime.RootFolder,
                    owned,
                    runtime.IsRunProcessActive);
                if (existing >= 0)
                {
                    peers[existing] = peer;
                }
                else
                {
                    peers.Add(peer);
                }
            }
        }

        var decision = ProjectPortIsolation.Decide(
            configuredOverride: null,
            profileUrls,
            peers);

        if (!decision.CanStart)
        {
            throw new InvalidOperationException(
                decision.BlockingPeer is null
                    ? "Could not allocate a non-conflicting application URL."
                    : $"Could not allocate ports; blocked by '{decision.BlockingPeer.DisplayName}' on port {decision.BlockingPort}.");
        }

        return decision.ApplicationUrl;
    }
}
