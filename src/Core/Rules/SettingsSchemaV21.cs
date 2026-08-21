using BuildMonitor.Core.Settings;

namespace BuildMonitor.Core.Rules;

/// <summary>Schema v20 flat projects → v21 nested Local attachments.</summary>
public static class SettingsSchemaV21
{
    public const int Version = 21;

    public static AppSettings FromLegacyV20(LegacyAppSettingsV20 legacy)
    {
        var settings = new AppSettings
        {
            SchemaVersion = Version,
            Connections = [],
            Monitor = legacy.Monitor,
            AppBehavior = legacy.AppBehavior,
            Projects = legacy.Projects.Select(ToMonitoredProject).ToList()
        };
        return settings;
    }

    public static MonitoredProjectSettings ToMonitoredProject(LegacyFlatProjectSettings flat) =>
        new()
        {
            Id = flat.Id,
            DisplayName = flat.DisplayName,
            IsActiveInSession = flat.IsActiveInSession,
            Azure = null,
            Local = new LocalProjectAttachment
            {
                RootFolder = flat.RootFolder,
                ProjectFile = flat.ProjectFile,
                LaunchProfile = flat.LaunchProfile,
                ExtraDotNetArgs = flat.ExtraDotNetArgs,
                TestProjectFile = flat.TestProjectFile,
                StartOnLaunch = flat.StartOnLaunch,
                BuildControlMode = flat.BuildControlMode,
                PreferredSiteUrlScheme = flat.PreferredSiteUrlScheme,
                RunOptions = flat.RunOptions
            }
        };

    public static MonitoredProjectSettings CreateLocalProject(
        string displayName,
        string rootFolder,
        string projectFile) =>
        new()
        {
            DisplayName = displayName,
            Local = new LocalProjectAttachment
            {
                RootFolder = rootFolder,
                ProjectFile = projectFile
            }
        };
}
