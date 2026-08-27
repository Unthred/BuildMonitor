using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Tests;

public sealed class SettingsBuildCliContextPresenterTests
{
    [Fact]
    public void Web_runnable_explains_launch_and_reports_web_endpoint()
    {
        var caps = SettingsProjectCapabilityPolicy.Evaluate(
            LocalProject(ProjectRunMode.Watch),
            launchProfilesAvailable: true,
            siteUrlApplicable: true);

        var view = SettingsBuildCliContextPresenter.Build(
            caps,
            launchProfilesDetected: true,
            webEndpointDetected: true,
            selectedOrPreferredLaunchProfile: "https",
            runMode: ProjectRunMode.Watch);

        Assert.True(view.ShowLaunchBehaviour);
        Assert.Contains("Launch profile", view.LaunchBehaviourBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preferred site URL", view.LaunchBehaviourBody, StringComparison.OrdinalIgnoreCase);
        Assert.True(view.ShowDetection);
        Assert.Contains(view.DetectionLines, l => l.Contains("Web endpoint", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(view.DetectionLines, l => l.Contains("Launch profile: https", StringComparison.Ordinal));
        Assert.Contains(view.DetectionLines, l => l.Contains("launchSettings.json", StringComparison.Ordinal));
        Assert.DoesNotContain(view.DetectionLines, l => l.Contains("not applicable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Non_web_does_not_claim_web_endpoint_and_explains_hidden_site_url()
    {
        var caps = SettingsProjectCapabilityPolicy.Evaluate(
            LocalProject(ProjectRunMode.Run),
            launchProfilesAvailable: true,
            siteUrlApplicable: false);

        var view = SettingsBuildCliContextPresenter.Build(
            caps,
            launchProfilesDetected: true,
            webEndpointDetected: false,
            selectedOrPreferredLaunchProfile: "BuildMonitor.TrayApp",
            runMode: ProjectRunMode.Run);

        Assert.True(view.ShowLaunchBehaviour);
        Assert.Contains("Site URL settings stay hidden", view.LaunchBehaviourBody, StringComparison.Ordinal);
        Assert.True(view.ShowDetection);
        Assert.Contains(view.DetectionLines, l => l.Contains("Desktop / non-web", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(view.DetectionLines, l => l.Contains("No web endpoint", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(view.DetectionLines, l => l.Contains("Site URL settings are not applicable", StringComparison.Ordinal));
        Assert.DoesNotContain(view.DetectionLines, l => l.Contains("Web endpoint available", StringComparison.Ordinal));
    }

    [Fact]
    public void Hidden_controls_and_explanatory_state_agree_for_run_mode_none()
    {
        var caps = SettingsProjectCapabilityPolicy.Evaluate(
            LocalProject(ProjectRunMode.None),
            launchProfilesAvailable: true,
            siteUrlApplicable: true);

        Assert.False(caps.LaunchProfilesAvailable);
        Assert.False(caps.SiteUrlApplicable);

        var view = SettingsBuildCliContextPresenter.Build(
            caps,
            launchProfilesDetected: true,
            webEndpointDetected: true,
            selectedOrPreferredLaunchProfile: "https",
            runMode: ProjectRunMode.None);

        Assert.Contains("does not launch", view.LaunchBehaviourBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stay hidden", view.LaunchBehaviourBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(view.DetectionLines, l => l.Contains("Build / monitor only", StringComparison.Ordinal));
        Assert.Contains(view.DetectionLines, l => l.Contains("are not shown", StringComparison.OrdinalIgnoreCase));
        // Capability says launch/site UI hidden — detection must not present them as active controls.
        Assert.DoesNotContain(view.DetectionLines, l => l.StartsWith("Launch profile:", StringComparison.Ordinal));
        Assert.DoesNotContain(view.DetectionLines, l => l.StartsWith("Site URL: resolved", StringComparison.Ordinal));
    }

    [Fact]
    public void Non_web_evidence_must_not_claim_web_when_detection_flag_false()
    {
        var caps = SettingsProjectCapabilityPolicy.Evaluate(
            LocalProject(ProjectRunMode.Watch),
            launchProfilesAvailable: false,
            siteUrlApplicable: false);

        var view = SettingsBuildCliContextPresenter.Build(
            caps,
            launchProfilesDetected: false,
            webEndpointDetected: false,
            selectedOrPreferredLaunchProfile: null,
            runMode: ProjectRunMode.Watch);

        Assert.DoesNotContain(view.DetectionLines, l => l.Contains("Web endpoint available", StringComparison.Ordinal));
        Assert.Contains(view.DetectionLines, l => l.Contains("No web endpoint", StringComparison.OrdinalIgnoreCase));
    }

    private static MonitoredProjectSettings LocalProject(ProjectRunMode mode) => new()
    {
        Id = "p1",
        DisplayName = "Sample",
        Local = new LocalProjectAttachment
        {
            RootFolder = @"C:\src\Sample",
            ProjectFile = "Sample.csproj",
            RunOptions = { RunMode = mode }
        }
    };
}
