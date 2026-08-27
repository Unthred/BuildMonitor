using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Tests;

public sealed class SettingsApplyImpactClassifierTests
{
    [Fact]
    public void Identical_settings_are_none_and_do_not_restart()
    {
        var settings = SampleSettings();
        var plan = SettingsApplyImpactClassifier.CreatePlan(settings, Clone(settings));
        Assert.Equal(SettingsApplyImpact.None, plan.Impact);
        Assert.False(plan.StopAllAndRestartActiveProjects);
        Assert.False(plan.ApplyOrchestratorSettings);
        Assert.False(plan.ShowProjectsStartingToast);
    }

    [Fact]
    public void TrayMenuLayout_only_is_presentation_with_zero_restarts()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.AppBehavior.TrayMenuLayout = TrayMenuLayout.ByProject;

        var plan = SettingsApplyImpactClassifier.CreatePlan(before, after);
        Assert.Equal(SettingsApplyImpact.Presentation, plan.Impact);
        Assert.False(plan.StopAllAndRestartActiveProjects);
        Assert.False(plan.ApplyOrchestratorSettings);
        Assert.False(plan.ResetHealthTransitionState);
        Assert.False(plan.ShowProjectsStartingToast);
    }

    [Fact]
    public void Theme_only_is_presentation()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.AppBehavior.Theme = AppThemePreference.Dark;

        Assert.Equal(
            SettingsApplyImpact.Presentation,
            SettingsApplyImpactClassifier.Classify(before, after));
    }

    [Fact]
    public void Azure_attachment_only_is_soft_runtime_without_local_restart()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.Projects[0].Azure = new AzureDevOpsProjectAttachment
        {
            ConnectionId = "c1",
            AdoProjectId = "p1",
            AdoProjectName = "P",
            RepositoryId = "r1",
            RepositoryName = "Repo"
        };

        var plan = SettingsApplyImpactClassifier.CreatePlan(before, after);
        Assert.Equal(SettingsApplyImpact.SoftRuntime, plan.Impact);
        Assert.False(plan.StopAllAndRestartActiveProjects);
        Assert.True(plan.ApplyOrchestratorSettings);
        Assert.False(plan.ShowProjectsStartingToast);
    }

    [Fact]
    public void Monitor_debounce_only_is_soft_runtime()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.Monitor.FileChangeDebounceMs = 9_000;

        var plan = SettingsApplyImpactClassifier.CreatePlan(before, after);
        Assert.Equal(SettingsApplyImpact.SoftRuntime, plan.Impact);
        Assert.False(plan.StopAllAndRestartActiveProjects);
    }

    [Fact]
    public void Local_project_file_change_is_hard_restart()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.Projects[0].Local!.ProjectFile = "Other.csproj";

        var plan = SettingsApplyImpactClassifier.CreatePlan(before, after);
        Assert.Equal(SettingsApplyImpact.HardRestart, plan.Impact);
        Assert.True(plan.StopAllAndRestartActiveProjects);
        Assert.True(plan.ApplyOrchestratorSettings);
        Assert.True(plan.ShowProjectsStartingToast);
    }

    [Fact]
    public void Active_session_toggle_is_hard_restart()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.Projects[0].IsActiveInSession = false;

        Assert.Equal(
            SettingsApplyImpact.HardRestart,
            SettingsApplyImpactClassifier.Classify(before, after));
    }

    [Fact]
    public void Ai_controlled_mode_change_is_hard_restart_preserving_build_policy_surface()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.Projects[0].Local!.BuildControlMode = ProjectBuildControlMode.AiControlled;

        Assert.Equal(
            SettingsApplyImpact.HardRestart,
            SettingsApplyImpactClassifier.Classify(before, after));
    }

    [Fact]
    public void Null_before_is_hard_restart_like_cold_start()
    {
        Assert.Equal(
            SettingsApplyImpact.HardRestart,
            SettingsApplyImpactClassifier.Classify(null, SampleSettings()));
    }

    [Fact]
    public void Display_name_only_is_soft_runtime()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.Projects[0].DisplayName = "Renamed";

        var plan = SettingsApplyImpactClassifier.CreatePlan(before, after);
        Assert.Equal(SettingsApplyImpact.SoftRuntime, plan.Impact);
        Assert.False(plan.StopAllAndRestartActiveProjects);
    }

    private static AppSettings SampleSettings() => new()
    {
        Projects =
        [
            new MonitoredProjectSettings
            {
                Id = "proj1",
                DisplayName = "WitherbyConnect (main)",
                IsActiveInSession = true,
                Local = new LocalProjectAttachment
                {
                    RootFolder = @"C:\src\WitherbyConnectDotNet9",
                    ProjectFile = "WitherbyConnect.csproj",
                    BuildControlMode = ProjectBuildControlMode.FileWatching,
                    StartOnLaunch = true
                }
            }
        ],
        Monitor = new GlobalMonitorSettings(),
        AppBehavior = new AppBehaviorSettings
        {
            TrayMenuLayout = TrayMenuLayout.ByOperation
        }
    };

    private static AppSettings Clone(AppSettings source) =>
        System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(source))
        ?? new AppSettings();
}
