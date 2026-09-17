using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Infrastructure.LocalBuild;

public static class ProjectRunContextFactory
{
    public static ProjectRunContext Capture(MonitoredProjectSettings project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var local = project.Local
            ?? throw new InvalidOperationException($"Project '{project.Id}' has no Local attachment.");

        if (string.IsNullOrWhiteSpace(local.RootFolder))
        {
            throw new InvalidOperationException($"Project '{project.Id}' has no root folder.");
        }

        var rootFolder = Path.GetFullPath(local.RootFolder);
        var startupProjectPath = Path.IsPathRooted(local.ProjectFile)
            ? Path.GetFullPath(local.ProjectFile)
            : Path.GetFullPath(Path.Combine(rootFolder, local.ProjectFile));
        var workingDirectory = Path.GetDirectoryName(startupProjectPath);
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            workingDirectory = rootFolder;
        }

        var launchSettingsPath = Path.Combine(workingDirectory, "Properties", "launchSettings.json");
        var launchProfile = LaunchProfileEnvironmentApplier.ResolveEffectiveLaunchProfile(
            rootFolder,
            local.ProjectFile,
            local.LaunchProfile);
        var profileUrls = LaunchProfileEnvironmentApplier.ResolveListenUrls(
            rootFolder,
            local.ProjectFile,
            launchProfile);
        var applicationUrlOverride = string.IsNullOrWhiteSpace(local.ApplicationUrl)
            ? null
            : local.ApplicationUrl.Trim();
        var effective = applicationUrlOverride
            ?? (profileUrls.Count == 0 ? null : ProjectPortIsolation.JoinApplicationUrl(profileUrls));

        return new ProjectRunContext(
            project.Id,
            project.DisplayName,
            rootFolder,
            startupProjectPath,
            workingDirectory,
            launchProfile,
            launchSettingsPath,
            applicationUrlOverride,
            profileUrls,
            effective,
            local.ExtraDotNetArgs ?? string.Empty);
    }
}
