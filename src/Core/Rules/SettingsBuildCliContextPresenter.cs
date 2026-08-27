using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Read-only Build CLI contextual copy for Projects Settings. Presentation only —
/// does not change capability detection or persisted settings.
/// </summary>
public sealed record SettingsBuildCliContextView(
    bool ShowLaunchBehaviour,
    string LaunchBehaviourTitle,
    string LaunchBehaviourBody,
    bool ShowDetection,
    string DetectionTitle,
    IReadOnlyList<string> DetectionLines);

/// <summary>
/// Builds muted inline help / detection summary for the Build CLI column from
/// <see cref="SettingsProjectCapabilities"/> plus launchSettings evidence.
/// </summary>
public static class SettingsBuildCliContextPresenter
{
    public static SettingsBuildCliContextView Build(
        SettingsProjectCapabilities caps,
        bool launchProfilesDetected,
        bool webEndpointDetected,
        string? selectedOrPreferredLaunchProfile,
        ProjectRunMode runMode)
    {
        if (!caps.HasLocalAttachment)
        {
            return new SettingsBuildCliContextView(
                ShowLaunchBehaviour: true,
                LaunchBehaviourTitle: "Launch behaviour",
                LaunchBehaviourBody:
                "Associate a local folder to configure how BuildMonitor launches and runs this project.",
                ShowDetection: false,
                DetectionTitle: "Detected application",
                DetectionLines: []);
        }

        var launchBody = BuildLaunchBehaviourBody(caps, runMode);
        var detectionLines = BuildDetectionLines(
            caps,
            launchProfilesDetected,
            webEndpointDetected,
            selectedOrPreferredLaunchProfile);

        return new SettingsBuildCliContextView(
            ShowLaunchBehaviour: true,
            LaunchBehaviourTitle: "Launch behaviour",
            LaunchBehaviourBody: launchBody,
            ShowDetection: detectionLines.Count > 0,
            DetectionTitle: "Detected application",
            DetectionLines: detectionLines);
    }

    private static string BuildLaunchBehaviourBody(
        SettingsProjectCapabilities caps,
        ProjectRunMode runMode)
    {
        if (caps.RunModeNone)
        {
            return
                "Run mode is None — BuildMonitor monitors and builds this project but does not launch it. " +
                "Launch profile and site URL controls stay hidden until a launch mode is selected.";
        }

        if (caps.LaunchProfilesAvailable && caps.SiteUrlApplicable)
        {
            return
                "These settings control how BuildMonitor launches the app after a successful or manual build. " +
                "Launch profile selects the configuration/environment. " +
                "Preferred site URL chooses which discovered web endpoint to display or open when applicable.";
        }

        if (caps.LaunchProfilesAvailable)
        {
            return
                "These settings control how BuildMonitor launches the app after a successful or manual build. " +
                "Launch profile selects the configuration/environment. " +
                "Site URL settings stay hidden when no web endpoint is detected.";
        }

        // Runnable but no launchSettings profiles (or empty discovery).
        return runMode switch
        {
            ProjectRunMode.Watch =>
                "Watch mode rebuilds on change and can start the app when configured. " +
                "No launch profiles were found under Properties/launchSettings.json.",
            ProjectRunMode.Run =>
                "Run mode starts the app once after a successful build. " +
                "No launch profiles were found under Properties/launchSettings.json.",
            _ =>
                "These settings control how BuildMonitor launches the application after a successful or manual build."
        };
    }

    private static IReadOnlyList<string> BuildDetectionLines(
        SettingsProjectCapabilities caps,
        bool launchProfilesDetected,
        bool webEndpointDetected,
        string? selectedOrPreferredLaunchProfile)
    {
        if (caps.RunModeNone)
        {
            return
            [
                "Build / monitor only (Run mode: None)",
                "Launch profile and site URL controls are not shown",
                webEndpointDetected
                    ? "Web endpoint present in launchSettings.json (unused while not launching)"
                    : "No web endpoint required for build-only monitoring"
            ];
        }

        if (webEndpointDetected && caps.SiteUrlApplicable)
        {
            var lines = new List<string>
            {
                "Web endpoint available",
            };
            if (!string.IsNullOrWhiteSpace(selectedOrPreferredLaunchProfile))
            {
                lines.Add($"Launch profile: {selectedOrPreferredLaunchProfile.Trim()}");
            }
            else if (launchProfilesDetected)
            {
                lines.Add("Launch profiles found in launchSettings.json");
            }

            lines.Add("Site URL: resolved from launchSettings.json");
            return lines;
        }

        // Non-web (or site URL not applicable while runnable).
        var nonWeb = new List<string>
        {
            "Desktop / non-web project",
            "No web endpoint detected",
            "Site URL settings are not applicable"
        };

        if (launchProfilesDetected && caps.LaunchProfilesAvailable)
        {
            if (!string.IsNullOrWhiteSpace(selectedOrPreferredLaunchProfile))
            {
                nonWeb.Insert(1, $"Launch profile: {selectedOrPreferredLaunchProfile.Trim()}");
            }
            else
            {
                nonWeb.Insert(1, "Launch profiles found (no applicationUrl)");
            }
        }
        else if (!launchProfilesDetected)
        {
            nonWeb.Insert(1, "No launchSettings.json profiles found");
        }

        return nonWeb;
    }
}
