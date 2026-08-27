using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Tests;

public sealed class SettingsProjectCapabilityPolicyTests
{
    [Fact]
    public void RunMode_None_hides_launch_site_and_restart_keeps_tests()
    {
        var project = LocalProject(ProjectRunMode.None);
        var caps = SettingsProjectCapabilityPolicy.Evaluate(
            project,
            launchProfilesAvailable: true,
            siteUrlApplicable: true);

        Assert.True(caps.HasLocalAttachment);
        Assert.True(caps.RunModeNone);
        Assert.False(caps.Runnable);
        Assert.False(caps.LaunchProfilesAvailable);
        Assert.False(caps.SiteUrlApplicable);
        Assert.False(caps.RestartApplicable);
        Assert.False(caps.WatchRestartApplicable);
        Assert.True(caps.WatchApplicable);
        Assert.True(caps.TestsApplicable);
    }

    [Fact]
    public void Non_web_runnable_shows_launch_not_site_url()
    {
        var project = LocalProject(ProjectRunMode.Run);
        var caps = SettingsProjectCapabilityPolicy.Evaluate(
            project,
            launchProfilesAvailable: true,
            siteUrlApplicable: false);

        Assert.True(caps.Runnable);
        Assert.True(caps.LaunchProfilesAvailable);
        Assert.False(caps.SiteUrlApplicable);
        Assert.True(caps.RestartApplicable);
        Assert.True(caps.TestsApplicable);
    }

    [Fact]
    public void Web_runnable_shows_launch_and_site_url()
    {
        var project = LocalProject(ProjectRunMode.Watch);
        var caps = SettingsProjectCapabilityPolicy.Evaluate(
            project,
            launchProfilesAvailable: true,
            siteUrlApplicable: true);

        Assert.True(caps.LaunchProfilesAvailable);
        Assert.True(caps.SiteUrlApplicable);
        Assert.True(caps.WatchRestartApplicable);
        Assert.True(caps.RestartApplicable);
    }

    [Fact]
    public void Azure_only_project_hides_local_capabilities()
    {
        var project = new MonitoredProjectSettings
        {
            DisplayName = "Azure only",
            Local = null,
            Azure = new AzureDevOpsProjectAttachment { ConnectionId = "c1" }
        };

        var caps = SettingsProjectCapabilityPolicy.Evaluate(project, true, true);
        Assert.False(caps.HasLocalAttachment);
        Assert.False(caps.TestsApplicable);
        Assert.False(caps.Runnable);
    }

    [Fact]
    public void Capability_visibility_does_not_require_hard_restart_classification()
    {
        // Presentation-only AppBehavior change remains Presentation under #87.
        var before = new AppSettings
        {
            Projects = [LocalProject(ProjectRunMode.None)],
            AppBehavior = new AppBehaviorSettings { TrayMenuLayout = TrayMenuLayout.ByOperation }
        };
        var after = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(before))!;
        after.AppBehavior.TrayMenuLayout = TrayMenuLayout.ByProject;

        Assert.Equal(
            SettingsApplyImpact.Presentation,
            SettingsApplyImpactClassifier.Classify(before, after));
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

public sealed class TestProjectPathRulesTests
{
    [Fact]
    public void Empty_test_path_is_valid()
    {
        Assert.True(TestProjectPathRules.IsValidForRoot(@"C:\src\BuildMonitor", ""));
        Assert.Equal("", TestProjectPathRules.SanitizeForRoot(@"C:\src\BuildMonitor", "  "));
    }

    [Fact]
    public void Foreign_relative_path_is_rejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "bm-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var foreign = @"WitherbyConnect.Tests\WitherbyConnect.Tests.csproj";
            Assert.False(TestProjectPathRules.IsValidForRoot(root, foreign));
            Assert.Equal("", TestProjectPathRules.SanitizeForRoot(root, foreign));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Owned_existing_test_project_is_accepted()
    {
        var root = Path.Combine(Path.GetTempPath(), "bm-root-" + Guid.NewGuid().ToString("N"));
        var testsDir = Path.Combine(root, "src", "BuildMonitor.Tests");
        Directory.CreateDirectory(testsDir);
        var testsProj = Path.Combine(testsDir, "BuildMonitor.Tests.csproj");
        File.WriteAllText(testsProj, "<Project />");
        try
        {
            var relative = Path.Combine("src", "BuildMonitor.Tests", "BuildMonitor.Tests.csproj");
            Assert.True(TestProjectPathRules.IsValidForRoot(root, relative));
            Assert.Equal(relative, TestProjectPathRules.SanitizeForRoot(root, relative));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Switching_projects_does_not_keep_foreign_test_path_on_sanitize()
    {
        var buildMonitor = new MonitoredProjectSettings
        {
            Id = "bm",
            DisplayName = "BuildMonitor.TrayApp",
            Local = new LocalProjectAttachment
            {
                RootFolder = @"C:\src\BuildMonitor",
                ProjectFile = @"src\TrayApp\BuildMonitor.TrayApp.csproj",
                TestProjectFile = @"WitherbyConnect.Tests\WitherbyConnect.Tests.csproj"
            }
        };

        buildMonitor.Local.TestProjectFile = TestProjectPathRules.SanitizeForRoot(
            buildMonitor.Local.RootFolder,
            buildMonitor.Local.TestProjectFile);

        Assert.Equal(string.Empty, buildMonitor.Local.TestProjectFile);
    }
}
