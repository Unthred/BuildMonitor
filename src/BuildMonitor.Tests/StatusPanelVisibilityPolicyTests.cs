using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Tests;

public sealed class StatusPanelVisibilityPolicyTests
{
    [Fact]
    public void Local_enabled_and_active_holds_visible()
    {
        var snapshots = new[] { LocalSnapshot(ProjectLifecycleState.Building) };

        Assert.True(StatusPanelVisibilityPolicy.HasLocalBuildActivityHold(snapshots, keepVisibleDuringLocalBuild: true));
        Assert.True(StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(snapshots, true, false));
    }

    [Fact]
    public void Local_disabled_and_active_does_not_hold()
    {
        var snapshots = new[] { LocalSnapshot(ProjectLifecycleState.Building) };

        Assert.False(StatusPanelVisibilityPolicy.HasLocalBuildActivityHold(snapshots, keepVisibleDuringLocalBuild: false));
        Assert.False(StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(snapshots, false, false));
    }

    [Fact]
    public void Azure_enabled_and_active_holds_visible()
    {
        var snapshots = new[] { AzureSnapshot(PipelineRunState.InProgress, AzureCiMonitoringState.Activity) };

        Assert.True(StatusPanelVisibilityPolicy.HasAzureBuildActivityHold(snapshots, keepVisibleDuringAzureBuild: true));
        Assert.True(StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(snapshots, false, true));
    }

    [Fact]
    public void Azure_disabled_and_active_does_not_hold()
    {
        var snapshots = new[] { AzureSnapshot(PipelineRunState.InProgress, AzureCiMonitoringState.Activity) };

        Assert.False(StatusPanelVisibilityPolicy.HasAzureBuildActivityHold(snapshots, keepVisibleDuringAzureBuild: false));
        Assert.False(StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(snapshots, false, false));
    }

    [Fact]
    public void Local_and_Azure_active_holds_when_either_enabled()
    {
        var snapshots = new[]
        {
            LocalSnapshot(ProjectLifecycleState.Building, "p1"),
            AzureSnapshot(PipelineRunState.InProgress, AzureCiMonitoringState.Activity, "p2")
        };

        Assert.True(StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(snapshots, true, true));
        Assert.True(StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(snapshots, true, false));
        Assert.True(StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(snapshots, false, true));
    }

    [Fact]
    public void Local_settles_while_Azure_remains_active_still_holds()
    {
        var snapshots = new[]
        {
            LocalSnapshot(ProjectLifecycleState.Watching, "p1"),
            AzureSnapshot(PipelineRunState.InProgress, AzureCiMonitoringState.Activity, "p2")
        };

        Assert.False(StatusPanelVisibilityPolicy.HasLocalBuildActivityHold(snapshots, true));
        Assert.True(StatusPanelVisibilityPolicy.HasAzureBuildActivityHold(snapshots, true));
        Assert.True(StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(snapshots, true, true));
    }

    [Fact]
    public void Azure_settles_while_Local_remains_active_still_holds()
    {
        var snapshots = new[]
        {
            LocalSnapshot(ProjectLifecycleState.Testing, "p1"),
            AzureSnapshot(PipelineRunState.Completed, AzureCiMonitoringState.Healthy, "p2")
        };

        Assert.True(StatusPanelVisibilityPolicy.HasLocalBuildActivityHold(snapshots, true));
        Assert.False(StatusPanelVisibilityPolicy.HasAzureBuildActivityHold(snapshots, true));
        Assert.True(StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(snapshots, true, true));
    }

    [Fact]
    public void Final_active_source_settles_releases_hold()
    {
        var snapshots = new[]
        {
            LocalSnapshot(ProjectLifecycleState.Watching, "p1"),
            AzureSnapshot(PipelineRunState.Completed, AzureCiMonitoringState.Healthy, "p2")
        };

        Assert.False(StatusPanelVisibilityPolicy.HasAnyBuildActivityHold(snapshots, true, true));
    }

    [Fact]
    public void Pointer_hover_reason_is_orchestrated_separately()
    {
        var reasons = StatusPanelVisibilityReason.PointerHover
            | StatusPanelVisibilityReason.LocalBuildActivity;

        Assert.True(StatusPanelVisibilityPolicy.ShouldRemainVisible(reasons));
        Assert.True(StatusPanelVisibilityPolicy.ShouldSuppressAutoHideForBuildActivity(reasons));

        var hoverOnly = StatusPanelVisibilityReason.PointerHover;
        Assert.True(StatusPanelVisibilityPolicy.ShouldRemainVisible(hoverOnly));
        Assert.False(StatusPanelVisibilityPolicy.ShouldSuppressAutoHideForBuildActivity(hoverOnly));
    }

    [Fact]
    public void Build_active_without_pointer_still_suppresses_auto_hide()
    {
        var reasons = StatusPanelVisibilityPolicy.EvaluateBuildActivityReasons(
            new[] { LocalSnapshot(ProjectLifecycleState.Building) },
            keepVisibleDuringLocalBuild: true,
            keepVisibleDuringAzureBuild: false);

        Assert.Equal(StatusPanelVisibilityReason.LocalBuildActivity, reasons);
        Assert.True(StatusPanelVisibilityPolicy.ShouldSuppressAutoHideForBuildActivity(reasons));
    }

    [Fact]
    public void All_reasons_clear_releases_visibility()
    {
        Assert.False(StatusPanelVisibilityPolicy.ShouldRemainVisible(StatusPanelVisibilityReason.None));
        Assert.False(StatusPanelVisibilityPolicy.ShouldSuppressAutoHideForBuildActivity(StatusPanelVisibilityReason.None));
    }

    [Fact]
    public void Activity_across_two_projects_aggregates_hold()
    {
        var snapshots = new[]
        {
            LocalSnapshot(ProjectLifecycleState.Watching, "p1"),
            LocalSnapshot(ProjectLifecycleState.Building, "p2")
        };

        Assert.True(StatusPanelVisibilityPolicy.HasLocalBuildActivityHold(snapshots, true));
    }

    [Fact]
    public void Settings_classified_as_presentation()
    {
        var before = new AppSettings
        {
            SchemaVersion = SettingsSchemaV23.Version,
            AppBehavior = new AppBehaviorSettings
            {
                KeepStatusVisibleDuringLocalBuildActivity = true,
                KeepStatusVisibleDuringAzureBuildActivity = true
            }
        };
        var after = Clone(before);
        after.AppBehavior.KeepStatusVisibleDuringLocalBuildActivity = false;

        Assert.Equal(SettingsApplyImpact.Presentation, SettingsApplyImpactClassifier.Classify(before, after));
    }

    [Fact]
    public void Missing_persisted_fields_default_both_visibility_settings_on()
    {
        const string json = """
            {
              "schemaVersion": 22,
              "appBehavior": {
                "theme": 0
              },
              "projects": []
            }
            """;

        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            json,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })!;
        if (settings.SchemaVersion < SettingsSchemaV23.Version)
        {
            settings.SchemaVersion = SettingsSchemaV23.Version;
        }

        Assert.True(settings.AppBehavior.KeepStatusVisibleDuringLocalBuildActivity);
        Assert.True(settings.AppBehavior.KeepStatusVisibleDuringAzureBuildActivity);
    }

    [Fact]
    public void Azure_auth_required_is_not_active()
    {
        var snapshot = AzureSnapshot(PipelineRunState.InProgress, AzureCiMonitoringState.Activity);
        var degraded = snapshot with
        {
            Azure = snapshot.Azure! with { Availability = AzureMonitoringAvailability.AuthRequired }
        };

        Assert.False(StatusPanelVisibilityPolicy.IsQualifyingAzureBuildActivity(degraded));
    }

    [Fact]
    public void Azure_unavailable_is_not_active()
    {
        var snapshot = AzureSnapshot(PipelineRunState.InProgress, AzureCiMonitoringState.Activity);
        var degraded = snapshot with
        {
            Azure = snapshot.Azure! with { Availability = AzureMonitoringAvailability.Unavailable }
        };

        Assert.False(StatusPanelVisibilityPolicy.IsQualifyingAzureBuildActivity(degraded));
    }

    [Theory]
    [InlineData(PipelineRunState.Completed, AzureCiMonitoringState.Failed)]
    [InlineData(PipelineRunState.Completed, AzureCiMonitoringState.Healthy)]
    public void Azure_settled_completed_state_is_not_active(
        PipelineRunState state,
        AzureCiMonitoringState ciState)
    {
        var snapshot = AzureSnapshot(state, ciState);
        Assert.False(StatusPanelVisibilityPolicy.IsQualifyingAzureBuildActivity(snapshot));
    }

    [Theory]
    [InlineData(PipelineRunState.NotStarted)]
    [InlineData(PipelineRunState.InProgress)]
    [InlineData(PipelineRunState.Canceling)]
    public void Azure_active_pipeline_states_hold_when_enabled(PipelineRunState state)
    {
        var snapshot = AzureSnapshot(state, AzureCiMonitoringState.Healthy);
        Assert.True(StatusPanelVisibilityPolicy.IsQualifyingAzureBuildActivity(snapshot));
        Assert.True(StatusPanelVisibilityPolicy.HasAzureBuildActivityHold(new[] { snapshot }, true));
    }

    [Fact]
    public void Local_testing_counts_as_qualifying_activity()
    {
        var snapshot = LocalSnapshot(ProjectLifecycleState.Testing);
        Assert.True(StatusPanelVisibilityPolicy.IsQualifyingLocalBuildActivity(snapshot));
    }

    [Fact]
    public void Local_restart_counts_as_qualifying_activity()
    {
        var snapshot = LocalSnapshot(ProjectLifecycleState.Watching, isRestarting: true);
        Assert.True(StatusPanelVisibilityPolicy.IsQualifyingLocalBuildActivity(snapshot));
    }

    [Fact]
    public void Inactive_project_does_not_contribute_to_hold()
    {
        var snapshot = LocalSnapshot(ProjectLifecycleState.Building, isActive: false);
        Assert.False(StatusPanelVisibilityPolicy.IsQualifyingLocalBuildActivity(snapshot));
    }

    private static AppSettings Clone(AppSettings source) =>
        System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(source),
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })!;

    private static ProjectHealthSnapshot LocalSnapshot(
        ProjectLifecycleState state,
        string projectId = "p1",
        bool isActive = true,
        bool isRestarting = false) =>
        new(
            projectId,
            "Demo",
            MonitorHealth.Amber,
            "Building",
            state,
            null,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            isActive,
            [],
            IsRestarting: isRestarting);

    [Fact]
    public void Azure_build_activity_hold_suppresses_auto_hide_without_site_ready_pin()
    {
        var snapshots = new[] { AzureSnapshot(PipelineRunState.InProgress, AzureCiMonitoringState.Activity) };
        var reasons = StatusPanelVisibilityPolicy.EvaluateBuildActivityReasons(snapshots, false, true);
        Assert.True(StatusPanelVisibilityPolicy.ShouldSuppressAutoHideForBuildActivity(reasons));
    }

    private static ProjectHealthSnapshot AzureSnapshot(
        PipelineRunState runState,
        AzureCiMonitoringState ciState,
        string projectId = "p1") =>
        new(
            projectId,
            "Demo",
            MonitorHealth.Amber,
            "Building",
            ProjectLifecycleState.Watching,
            null,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            true,
            [],
            Azure: new ProjectAzureHealthFacet(
                AzureMonitoringAvailability.Available,
                ciState,
                FocusBranch: "master",
                PrimaryRun: new AzurePipelineRunInfo(
                    DefinitionId: 1,
                    PipelineDisplayName: "CI",
                    RunId: 42,
                    BuildNumber: "1.0",
                    State: runState,
                    Result: runState == PipelineRunState.Completed
                        ? PipelineRunResult.Succeeded
                        : PipelineRunResult.Unknown,
                    Branch: "master",
                    QueuedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
                    StartedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-4),
                    FinishedAtUtc: runState == PipelineRunState.Completed
                        ? DateTimeOffset.UtcNow
                        : null,
                    RunUrl: "https://dev.azure.com/org/proj/_build/results?buildId=42"),
                AttentionRuns: [],
                PolledAtUtc: DateTimeOffset.UtcNow,
                HasSelectedPipelines: true));
}
