using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class StatusPanelPresentationBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 6, 39, 0, TimeSpan.Zero);

    [Fact]
    public void Build_hides_site_ready_while_building()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.Building,
            listenUrl: "http://localhost:5154",
            listenUrlReady: true,
            supportsRestart: true,
            isRestarting: true);

        var presentation = StatusPanelPresentationBuilder.Build([snapshot], panelDismissAtUtc: null, Now);

        var card = Assert.Single(presentation.Cards);
        Assert.False(card.ShowSiteReady);
        Assert.False(card.ShowSiteAwaiting);
        Assert.Equal(StatusPanelSideRailMode.Accent, presentation.SideRail.Mode);
    }

    [Fact]
    public void Build_side_rail_and_card_agree_during_site_awaiting()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.Watching,
            listenUrl: "http://localhost:5154",
            listenUrlReady: false,
            supportsRestart: true);

        var presentation = StatusPanelPresentationBuilder.Build([snapshot], null, Now);

        Assert.False(Assert.Single(presentation.Cards).ShowSiteReady);
        Assert.True(Assert.Single(presentation.Cards).ShowSiteAwaiting);
        Assert.Equal("Starting site", presentation.SideRail.ActivityLabel);
    }

    [Fact]
    public void Build_still_edits_button_when_rebuild_countdown_active()
    {
        var waiting = StatusPanelPresentationBuilder.Build(
            [Snapshot(
                ProjectLifecycleState.WaitingForEdits,
                rebuildQuietUntilUtc: Now.AddSeconds(8))],
            null,
            Now);

        Assert.False(Assert.Single(waiting.Cards).ShowStillEditingButton);
        Assert.Equal("p1", waiting.HeaderStillEditingProjectId);
        Assert.Contains(
            "extend",
            waiting.HeaderStillEditingToolTip!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_still_edits_button_marks_unexpected_while_building()
    {
        var building = StatusPanelPresentationBuilder.Build(
            [Snapshot(ProjectLifecycleState.Building)],
            null,
            Now);

        var card = Assert.Single(building.Cards);
        Assert.False(card.ShowStillEditingButton);
        Assert.Equal("p1", building.HeaderStillEditingProjectId);
        Assert.Contains(
            "unexpected",
            building.HeaderStillEditingToolTip!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_still_edits_button_hidden_when_no_countdown_or_build()
    {
        var watching = StatusPanelPresentationBuilder.Build(
            [Snapshot(ProjectLifecycleState.Watching)],
            null,
            Now);

        Assert.False(Assert.Single(watching.Cards).ShowStillEditingButton);
    }

    [Fact]
    public void Build_header_countdown_prefers_closing_over_rebuild()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.WaitingForEdits,
            rebuildQuietUntilUtc: Now.AddSeconds(12));

        var presentation = StatusPanelPresentationBuilder.Build(
            [snapshot],
            panelDismissAtUtc: Now.AddSeconds(4),
            Now);

        Assert.Equal("Closing in 4 s", presentation.HeaderCountdownText);
    }

    [Fact]
    public void Healthy_card_has_build_and_last_build_only()
    {
        var card = StatusPanelPresentationBuilder.Build(
            [Snapshot(ProjectLifecycleState.Watching, health: MonitorHealth.Green, healthLabel: "Healthy")],
            null,
            Now).Cards[0];

        Assert.Equal(["MODE", "BUILD", "LAST BUILD"], card.StatusRows.Select(r => r.Label).ToArray());
        Assert.Equal("File Watching", Row(card, "MODE").Primary);
        Assert.Equal("✓ Succeeded", Row(card, "BUILD").Primary);
        Assert.Equal("0 errors · 0 warnings", Row(card, "BUILD").Secondary);
        Assert.Null(card.CurrentActionText);
        Assert.DoesNotContain(card.StatusRows, r => r.Label == "AGENT");
    }

    [Fact]
    public void Warnings_use_thousands_separators_once_on_build_row()
    {
        var card = StatusPanelPresentationBuilder.Build(
            [Snapshot(ProjectLifecycleState.WaitingForEdits, health: MonitorHealth.Amber, healthLabel: "Warnings", warningCount: 1013)],
            null,
            Now).Cards[0];

        Assert.Equal("Waiting", Row(card, "BUILD").Primary);
        Assert.Equal("0 errors · 1,013 warnings", Row(card, "BUILD").Secondary);
        Assert.Equal(StatusPanelRowEmphasis.Warning, Row(card, "BUILD").Emphasis);
    }

    [Fact]
    public void Build_failure_emphasizes_error_without_agent_headline_duplication()
    {
        var card = StatusPanelPresentationBuilder.Build(
            [Snapshot(
                ProjectLifecycleState.BuildFailed,
                health: MonitorHealth.Red,
                healthLabel: "Failed",
                errorCount: 2,
                warningCount: 12,
                errorPreview: "AccountInfoSection.razor · CS8780")],
            null,
            Now).Cards[0];

        Assert.Equal("Build failed", Row(card, "BUILD").Primary);
        Assert.Equal("2 errors · 12 warnings", Row(card, "BUILD").Secondary);
        Assert.Equal(StatusPanelRowEmphasis.Error, Row(card, "BUILD").Emphasis);
        Assert.True(card.ShowErrorPreview);
        Assert.Equal("AccountInfoSection.razor · CS8780", card.ErrorPreview);
    }

    [Fact]
    public void Active_building_shows_percent_when_steps_available()
    {
        var steps = new[]
        {
            new BuildProgressStep("Restore", BuildStepStatus.Complete),
            new BuildProgressStep("Compile", BuildStepStatus.Active),
            new BuildProgressStep("Copy", BuildStepStatus.Pending)
        };
        var card = StatusPanelPresentationBuilder.Build(
            [Snapshot(ProjectLifecycleState.Building, progressSteps: steps)],
            null,
            Now).Cards[0];

        Assert.Equal("Building · 33%", Row(card, "BUILD").Primary);
        Assert.True(card.ShowProgressChart);
    }

    [Fact]
    public void Agent_busy_with_queued_changes_is_structured()
    {
        var controlPlane = new ProjectControlPlaneSnapshot(
            SessionApiUsed: true,
            EffectiveSessionState: ControlPlaneSessionState.Busy,
            SessionSinceUtc: Now.AddMinutes(-3),
            AutoBuildBlockedBySession: true,
            HasPendingFileChangeRebuild: true,
            PendingFileChangeCount: 1,
            ShipCheckPhase: ControlPlaneShipCheckPhase.None,
            LastShipCheckOutcome: ControlPlaneShipCheckOutcome.None,
            LastShipCheckCompletedUtc: null,
            ShipCheckInProgress: false);

        var card = StatusPanelPresentationBuilder.Build(
            [Snapshot(
                ProjectLifecycleState.WaitingForEdits,
                health: MonitorHealth.Amber,
                healthLabel: "Warnings",
                warningCount: 1013,
                controlPlane: controlPlane,
                editGatingDetail: "Wait timer reset — 1 file(s) just saved (!). Quiet period restarted.",
                isEditGatingActive: true)],
            null,
            Now).Cards[0];

        Assert.Equal(["MODE", "BUILD", "AGENT", "CHANGES", "LAST BUILD"], card.StatusRows.Select(r => r.Label).ToArray());
        Assert.Equal("File Watching", Row(card, "MODE").Primary);
        Assert.Equal("Busy", Row(card, "AGENT").Primary);
        Assert.Equal("Builds paused · 3m", Row(card, "AGENT").Secondary);
        Assert.Equal(StatusPanelRowEmphasis.Busy, Row(card, "AGENT").Emphasis);
        Assert.Equal("1 queued", Row(card, "CHANGES").Primary);
        Assert.Equal("Quiet period restarted", Row(card, "CHANGES").Secondary);
        Assert.DoesNotContain(
            card.StatusRows,
            r => r.Primary.Contains("Agent editing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Idle_agent_omits_changes_when_none_pending()
    {
        var controlPlane = new ProjectControlPlaneSnapshot(
            SessionApiUsed: true,
            EffectiveSessionState: ControlPlaneSessionState.Idle,
            SessionSinceUtc: Now.AddMinutes(-10),
            AutoBuildBlockedBySession: false,
            HasPendingFileChangeRebuild: false,
            PendingFileChangeCount: 0,
            ShipCheckPhase: ControlPlaneShipCheckPhase.None,
            LastShipCheckOutcome: ControlPlaneShipCheckOutcome.None,
            LastShipCheckCompletedUtc: null,
            ShipCheckInProgress: false);

        var card = StatusPanelPresentationBuilder.Build(
            [Snapshot(ProjectLifecycleState.Watching, controlPlane: controlPlane)],
            null,
            Now).Cards[0];

        Assert.Equal(["MODE", "BUILD", "AGENT", "LAST BUILD"], card.StatusRows.Select(r => r.Label).ToArray());
        Assert.Equal("File Watching", Row(card, "MODE").Primary);
        Assert.Equal("Idle", Row(card, "AGENT").Primary);
        Assert.Equal("Build allowed", Row(card, "AGENT").Secondary);
    }

    [Fact]
    public void Quiet_period_waiting_shows_remaining_on_changes()
    {
        var card = StatusPanelPresentationBuilder.Build(
            [Snapshot(
                ProjectLifecycleState.WaitingForEdits,
                isEditGatingActive: true,
                rebuildQuietUntilUtc: Now.AddSeconds(2),
                editGatingDetail: "Waiting 7 s after the last save.")],
            null,
            Now).Cards[0];

        Assert.Equal("Settling", Row(card, "CHANGES").Primary);
        Assert.Equal("2s remaining", Row(card, "CHANGES").Secondary);
    }

    [Fact]
    public void Ship_check_testing_uses_build_override_and_transient_action()
    {
        var controlPlane = new ProjectControlPlaneSnapshot(
            SessionApiUsed: true,
            EffectiveSessionState: ControlPlaneSessionState.Idle,
            SessionSinceUtc: Now.AddMinutes(-1),
            AutoBuildBlockedBySession: false,
            HasPendingFileChangeRebuild: false,
            PendingFileChangeCount: 0,
            ShipCheckPhase: ControlPlaneShipCheckPhase.Testing,
            LastShipCheckOutcome: ControlPlaneShipCheckOutcome.None,
            LastShipCheckCompletedUtc: null,
            ShipCheckInProgress: true);

        var card = StatusPanelPresentationBuilder.Build(
            [Snapshot(ProjectLifecycleState.Testing, controlPlane: controlPlane)],
            null,
            Now).Cards[0];

        Assert.Equal("Ship check · Testing", Row(card, "BUILD").Primary);
        Assert.Equal("Running tests…", card.CurrentActionText);
    }

    [Fact]
    public void Rebuild_building_uses_build_override()
    {
        var controlPlane = new ProjectControlPlaneSnapshot(
            SessionApiUsed: true,
            EffectiveSessionState: ControlPlaneSessionState.Idle,
            SessionSinceUtc: Now.AddMinutes(-1),
            AutoBuildBlockedBySession: false,
            HasPendingFileChangeRebuild: false,
            PendingFileChangeCount: 0,
            ShipCheckPhase: ControlPlaneShipCheckPhase.None,
            LastShipCheckOutcome: ControlPlaneShipCheckOutcome.None,
            LastShipCheckCompletedUtc: null,
            ShipCheckInProgress: false,
            AgentRebuildInProgress: true,
            AgentRebuildPhase: ControlPlaneShipCheckPhase.Building);

        var card = StatusPanelPresentationBuilder.Build(
            [Snapshot(ProjectLifecycleState.Building, controlPlane: controlPlane)],
            null,
            Now).Cards[0];

        Assert.Equal("Agent rebuild · Building", Row(card, "BUILD").Primary);
        Assert.Equal("Rebuilding…", card.CurrentActionText);
    }

    [Fact]
    public void Last_build_includes_relative_time()
    {
        var card = StatusPanelPresentationBuilder.Build(
            [Snapshot(
                ProjectLifecycleState.Watching,
                lastBuildFinishedAtUtc: Now.AddMinutes(-4))],
            null,
            Now).Cards[0];

        Assert.Equal("4m ago", Row(card, "LAST BUILD").Secondary);
    }

    [Fact]
    public void Mode_row_always_present()
    {
        var card = StatusPanelPresentationBuilder.Build(
            [Snapshot(ProjectLifecycleState.Watching)],
            null,
            Now).Cards[0];

        Assert.Equal("File Watching", Row(card, "MODE").Primary);
        Assert.Equal("MODE", card.StatusRows[0].Label);
    }

    [Fact]
    public void Ai_controlled_pending_changes_show_awaiting_explicit_build()
    {
        var card = StatusPanelPresentationBuilder.Build(
            [Snapshot(
                ProjectLifecycleState.Idle,
                controlPlane: new ProjectControlPlaneSnapshot(
                    SessionApiUsed: true,
                    EffectiveSessionState: ControlPlaneSessionState.Idle,
                    SessionSinceUtc: Now.AddSeconds(-5),
                    AutoBuildBlockedBySession: false,
                    HasPendingFileChangeRebuild: true,
                    PendingFileChangeCount: 7,
                    ShipCheckPhase: ControlPlaneShipCheckPhase.None,
                    LastShipCheckOutcome: ControlPlaneShipCheckOutcome.None,
                    LastShipCheckCompletedUtc: null,
                    ShipCheckInProgress: false,
                    IdleCause: ControlPlaneIdleCause.Agent,
                    BuildControlMode: ProjectBuildControlMode.AiControlled,
                    AutoBuildEnabled: false))],
            null,
            Now).Cards[0];

        Assert.Equal("AI Controlled", Row(card, "MODE").Primary);
        Assert.Equal("7 detected", Row(card, "CHANGES").Primary);
        Assert.Equal("Awaiting explicit build", Row(card, "CHANGES").Secondary);
    }

    [Fact]
    public void Local_succeeded_azure_failed_keeps_local_build_succeeded_and_overall_red()
    {
        var now = Now;
        var azureRun = new AzurePipelineRunInfo(
            8,
            "WitherbyConnect",
            454,
            "20260825.15",
            PipelineRunState.Completed,
            PipelineRunResult.Failed,
            "PR #168",
            now.AddMinutes(-10),
            now.AddMinutes(-10),
            now.AddMinutes(-5),
            "https://example/?buildId=454",
            168);
        var azure = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Failed,
            "master",
            azureRun,
            [],
            now,
            HasSelectedPipelines: true);

        var local = Snapshot(
            ProjectLifecycleState.Watching,
            health: MonitorHealth.Green,
            healthLabel: "Success");

        // Composite Red must not rewrite LOCAL Build — simulate HealthCoalescer merge.
        var merged = ProjectHealthComposer.WithAzure(
            local with { LastBuildExitCode = 0 },
            azure);

        Assert.Equal(MonitorHealth.Red, merged.Health);
        Assert.Equal("Failed", merged.HealthLabel);

        var presentation = StatusPanelPresentationBuilder.Build([merged], null, now);
        var card = Assert.Single(presentation.Cards);

        Assert.Equal("✓ Succeeded", Row(card, "BUILD").Primary);
        Assert.Equal(StatusPanelRowEmphasis.Normal, Row(card, "BUILD").Emphasis);
        Assert.Equal(MonitorHealth.Green, StatusPanelPresentationBuilder.ResolveLocalBuildHealth(merged));

        Assert.NotNull(card.Azure);
        Assert.Equal("AZURE DEVOPS", card.Azure.HeaderLabel);
        Assert.True(card.Azure.ShowTable);
        Assert.Equal("✕", card.Azure.Rows[0].StatusGlyph);
        Assert.Equal("Failed", card.Azure.Rows[0].StatusText);
        Assert.Equal("#454", card.Azure.Rows[0].RunDisplay);
        Assert.Equal("20260825.15", card.Azure.Rows[0].BuildNumberDisplay);

        Assert.Equal(MonitorHealth.Red, presentation.SideRail.IdleHealth);
        Assert.Equal("Needs fix", presentation.SideRail.IdleLabel);
        Assert.Equal(MonitorHealth.Red, card.Health);
        Assert.Equal(
            MonitorHealth.Red,
            LocalTrayIconRollupEvaluator.Rollup([merged]));
    }

    [Fact]
    public void Status_panel_window_width_is_wide_enough_for_azure_table()
    {
        Assert.Equal(620, StatusPanelMetrics.WindowWidth);
        Assert.Equal(600, StatusPanelMetrics.WindowMinWidth);
        Assert.Equal(640, StatusPanelMetrics.WindowMaxWidth);
        Assert.True(StatusPanelMetrics.ContentMeasureWidth > 500);
    }

    private static StatusPanelStatusRow Row(StatusPanelCardPresentation card, string label) =>
        Assert.Single(card.StatusRows, r => r.Label == label);

    private static ProjectHealthSnapshot Snapshot(
        ProjectLifecycleState state,
        string? listenUrl = null,
        bool listenUrlReady = false,
        bool supportsRestart = false,
        bool isRestarting = false,
        DateTimeOffset? rebuildQuietUntilUtc = null,
        MonitorHealth health = MonitorHealth.Green,
        string healthLabel = "Healthy",
        int errorCount = 0,
        int warningCount = 0,
        string? errorPreview = null,
        IReadOnlyList<BuildProgressStep>? progressSteps = null,
        ProjectControlPlaneSnapshot? controlPlane = null,
        string? editGatingDetail = null,
        bool isEditGatingActive = false,
        DateTimeOffset? lastBuildFinishedAtUtc = null) =>
        new(
            ProjectId: "p1",
            DisplayName: "Demo",
            Health: health,
            HealthLabel: healthLabel,
            State: state,
            LastExitCode: 0,
            LastDuration: TimeSpan.FromSeconds(1),
            LastErrorPreview: errorPreview,
            ErrorCount: errorCount,
            WarningCount: warningCount,
            LastChangedUtc: Now,
            LastBuildFinishedAtUtc: lastBuildFinishedAtUtc ?? Now.AddMinutes(-4),
            IsActive: true,
            ProgressSteps: progressSteps ?? [],
            ListenUrl: listenUrl,
            ListenUrlReady: listenUrlReady,
            SupportsAppRestart: supportsRestart,
            IsRestarting: isRestarting,
            IsEditGatingActive: isEditGatingActive,
            EditGatingDetailText: editGatingDetail,
            RebuildQuietUntilUtc: rebuildQuietUntilUtc,
            ControlPlane: controlPlane);
}
