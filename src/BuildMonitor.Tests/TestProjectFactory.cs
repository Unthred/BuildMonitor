using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Tests;

internal static class TestProjectFactory
{
    public static MonitoredProjectSettings LocalOnly(
        string displayName = "Demo",
        string? id = null,
        string? rootFolder = null,
        string? projectFile = null,
        bool isActive = true,
        ProjectBuildControlMode buildControlMode = ProjectBuildControlMode.FileWatching,
        ProjectRunOptions? runOptions = null,
        string? launchProfile = null)
    {
        var root = rootFolder ?? Path.GetTempPath();
        return new MonitoredProjectSettings
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            DisplayName = displayName,
            IsActiveInSession = isActive,
            Local = new LocalProjectAttachment
            {
                RootFolder = root,
                ProjectFile = projectFile ?? Path.Combine(root, "Demo.csproj"),
                LaunchProfile = launchProfile ?? string.Empty,
                BuildControlMode = buildControlMode,
                RunOptions = runOptions ?? new ProjectRunOptions()
            }
        };
    }
}
