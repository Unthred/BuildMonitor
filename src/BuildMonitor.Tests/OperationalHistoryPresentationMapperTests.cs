using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class OperationalHistoryPresentationMapperTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void Maps_source_labels_without_enum_names()
    {
        Assert.Equal("Local", OperationalHistoryPresentationMapper.FormatSourceLabel(OperationalEventSource.Local));
        Assert.Equal("Azure", OperationalHistoryPresentationMapper.FormatSourceLabel(OperationalEventSource.Azure));
        Assert.Equal("Agent", OperationalHistoryPresentationMapper.FormatSourceLabel(OperationalEventSource.Agent));
        Assert.Equal("User", OperationalHistoryPresentationMapper.FormatSourceLabel(OperationalEventSource.User));
        Assert.Equal("System", OperationalHistoryPresentationMapper.FormatSourceLabel(OperationalEventSource.System));
    }

    [Fact]
    public void Maps_outcomes_to_emphasis()
    {
        Assert.Equal(StatusPanelRowEmphasis.Error, OperationalHistoryPresentationMapper.MapEmphasis(OperationalEventOutcome.Failed));
        Assert.Equal(StatusPanelRowEmphasis.Success, OperationalHistoryPresentationMapper.MapEmphasis(OperationalEventOutcome.Succeeded));
        Assert.Equal(StatusPanelRowEmphasis.Warning, OperationalHistoryPresentationMapper.MapEmphasis(OperationalEventOutcome.Cancelled));
        Assert.Equal(StatusPanelRowEmphasis.Busy, OperationalHistoryPresentationMapper.MapEmphasis(OperationalEventOutcome.Started));
        Assert.Equal(StatusPanelRowEmphasis.Normal, OperationalHistoryPresentationMapper.MapEmphasis(OperationalEventOutcome.Changed));
    }

    [Fact]
    public void Formats_health_transition_text()
    {
        var entry = Event(
            OperationalEventSource.System,
            OperationalEventKind.HealthTransition,
            OperationalEventOutcome.Changed,
            "ignored",
            previous: "Green",
            next: "Amber");
        Assert.Equal("Health Green → Amber", OperationalHistoryPresentationMapper.FormatPrimaryText(entry));
    }

    [Fact]
    public void Formats_azure_run_success()
    {
        var entry = Event(
            OperationalEventSource.Azure,
            OperationalEventKind.AzureRun,
            OperationalEventOutcome.Succeeded,
            "raw",
            azureRunId: 550,
            azureBuild: "550_Master");
        Assert.Equal("Run #550_Master succeeded", OperationalHistoryPresentationMapper.FormatPrimaryText(entry));
    }

    [Fact]
    public void Formats_test_failure_with_count()
    {
        var entry = Event(
            OperationalEventSource.Local,
            OperationalEventKind.Tests,
            OperationalEventOutcome.Failed,
            "raw",
            detail: new OperationalEventDetail(TestFailedCount: 2, FailingTestNames: ["A", "B"]));
        Assert.Equal("Tests failed · 2 failing", OperationalHistoryPresentationMapper.FormatPrimaryText(entry));
    }

    [Fact]
    public void Formats_explicit_ship_check()
    {
        var entry = Event(
            OperationalEventSource.Agent,
            OperationalEventKind.ExplicitAction,
            OperationalEventOutcome.Started,
            "Ship-check",
            detail: new OperationalEventDetail(ActionName: "ship-check"));
        Assert.Equal("Ship-check requested", OperationalHistoryPresentationMapper.FormatPrimaryText(entry));
    }

    [Fact]
    public void Empty_and_unavailable_sections()
    {
        var empty = OperationalHistoryPresentationMapper.BuildSection(
            storeAvailable: true, [], 10, expandByDefault: true, DateTimeOffset.UtcNow, Utc);
        Assert.Equal(OperationalHistoryAvailability.Empty, empty.Availability);
        Assert.Equal("No recent activity yet", empty.EmptyMessage);

        var unavailable = OperationalHistoryPresentationMapper.BuildSection(
            storeAvailable: false, null, 10, expandByDefault: true, DateTimeOffset.UtcNow, Utc);
        Assert.Equal(OperationalHistoryAvailability.Unavailable, unavailable.Availability);
        Assert.Equal("Recent activity unavailable", unavailable.UnavailableMessage);
    }

    [Fact]
    public void Orders_newest_first_and_respects_limit()
    {
        var older = Event(
            OperationalEventSource.Local,
            OperationalEventKind.Build,
            OperationalEventOutcome.Succeeded,
            "old",
            id: "1",
            at: DateTimeOffset.Parse("2026-09-04T08:00:00Z"));
        var newer = Event(
            OperationalEventSource.Local,
            OperationalEventKind.Build,
            OperationalEventOutcome.Failed,
            "new",
            id: "2",
            at: DateTimeOffset.Parse("2026-09-04T09:00:00Z"));

        // Caller supplies newest-first (store contract).
        var section = OperationalHistoryPresentationMapper.BuildSection(
            true, [newer, older], limit: 1, expandByDefault: false, DateTimeOffset.UtcNow, Utc);

        Assert.Equal(OperationalHistoryAvailability.Available, section.Availability);
        Assert.Single(section.Rows);
        Assert.Equal("2", section.Rows[0].EventId);
        Assert.Equal("Build failed", section.Rows[0].PrimaryText);
    }

    [Fact]
    public void Project_filter_is_caller_owned_via_input_list()
    {
        var p1 = Event(
            OperationalEventSource.Local,
            OperationalEventKind.Build,
            OperationalEventOutcome.Succeeded,
            "p1",
            id: "a",
            projectId: "p1");
        var section = OperationalHistoryPresentationMapper.BuildSection(
            true, [p1], 10, true, DateTimeOffset.UtcNow, Utc);
        Assert.All(section.Rows, r => Assert.Equal("a", r.EventId));
    }

    [Fact]
    public void Row_includes_detail_and_tooltip_without_raw_enums()
    {
        var entry = Event(
            OperationalEventSource.Local,
            OperationalEventKind.Build,
            OperationalEventOutcome.Failed,
            "OperationalEventKind.Build Outcome=Failed",
            detail: new OperationalEventDetail(ErrorPreview: "CS1002 expected", ExitCode: 1));
        var row = OperationalHistoryPresentationMapper.MapRow(entry, DateTimeOffset.UtcNow, Utc);
        Assert.Equal("Build failed", row.PrimaryText);
        Assert.Contains("CS1002", row.DetailText);
        Assert.DoesNotContain("OperationalEventKind", row.PrimaryText);
        Assert.DoesNotContain("Outcome=", row.ToolTip);
        Assert.Contains("Local", row.ToolTip);
    }

    [Fact]
    public void Local_time_label_uses_HH_mm_for_same_day()
    {
        var now = DateTimeOffset.Parse("2026-09-04T12:00:00Z");
        var entry = Event(
            OperationalEventSource.User,
            OperationalEventKind.ExplicitAction,
            OperationalEventOutcome.Started,
            "Rebuild",
            detail: new OperationalEventDetail(ActionName: "rebuild"),
            at: DateTimeOffset.Parse("2026-09-04T09:14:00Z"));
        var row = OperationalHistoryPresentationMapper.MapRow(entry, now, Utc);
        Assert.Equal("09:14", row.TimeLabel);
    }

    [Fact]
    public void Status_panel_builder_attaches_recent_activity()
    {
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Proj",
            MonitorHealth.Green,
            "Healthy",
            ProjectLifecycleState.Running,
            0,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            true,
            []);
        var events = new[]
        {
            Event(
                OperationalEventSource.Local,
                OperationalEventKind.Build,
                OperationalEventOutcome.Succeeded,
                "ok",
                projectId: "p1")
        };

        var presentation = StatusPanelPresentationBuilder.Build(
            [snapshot],
            null,
            DateTimeOffset.UtcNow,
            (id, limit) => events.Where(e => e.ProjectId == id).Take(limit).ToList(),
            historyStoreAvailable: true);

        Assert.Single(presentation.Cards);
        Assert.NotNull(presentation.Cards[0].RecentActivity);
        Assert.Equal(OperationalHistoryAvailability.Available, presentation.Cards[0].RecentActivity!.Availability);
        Assert.True(presentation.Cards[0].RecentActivity.ExpandByDefault);
    }

    [Fact]
    public void Unavailable_store_on_status_card()
    {
        var snapshot = new ProjectHealthSnapshot(
            "p1",
            "Proj",
            MonitorHealth.Green,
            "Healthy",
            ProjectLifecycleState.Running,
            0,
            null,
            null,
            0,
            0,
            DateTimeOffset.UtcNow,
            null,
            true,
            []);
        var presentation = StatusPanelPresentationBuilder.Build(
            [snapshot],
            null,
            DateTimeOffset.UtcNow,
            recentHistoryForProject: null,
            historyStoreAvailable: false);
        Assert.Equal(
            OperationalHistoryAvailability.Unavailable,
            presentation.Cards[0].RecentActivity!.Availability);
    }

    private static OperationalEvent Event(
        OperationalEventSource source,
        OperationalEventKind kind,
        OperationalEventOutcome outcome,
        string summary,
        string id = "e1",
        string projectId = "p1",
        DateTimeOffset? at = null,
        string? previous = null,
        string? next = null,
        long? azureRunId = null,
        string? azureBuild = null,
        OperationalEventDetail? detail = null) =>
        new(
            OperationalHistorySchema.CurrentVersion,
            id,
            projectId,
            at ?? DateTimeOffset.UtcNow,
            source,
            kind,
            outcome,
            summary,
            detail,
            OperationId: null,
            BuildTriggerId: null,
            LocalBuildNumber: null,
            AzureRunId: azureRunId,
            AzureBuildNumber: azureBuild,
            Branch: null,
            PreviousValue: previous,
            NewValue: next);
}
