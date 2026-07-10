using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class StatusPanelHeaderCountdownFormatterTests
{
    [Fact]
    public void Format_prefers_closing_over_rebuild_countdown()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshots = new[]
        {
            Snapshot(rebuildQuietUntilUtc: now.AddSeconds(30))
        };

        var text = StatusPanelHeaderCountdownFormatter.Format(
            snapshots,
            panelDismissAtUtc: now.AddSeconds(5),
            now);

        Assert.Equal("Closing in 5 s", text);
    }

    [Fact]
    public void Format_shows_rebuild_when_no_dismiss()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshots = new[]
        {
            Snapshot(rebuildQuietUntilUtc: now.AddSeconds(8))
        };

        var text = StatusPanelHeaderCountdownFormatter.Format(snapshots, panelDismissAtUtc: null, now);

        Assert.Equal("Rebuild in 8 s", text);
    }

    [Fact]
    public void Format_empty_when_no_timers()
    {
        var snapshots = new[] { Snapshot() };

        var text = StatusPanelHeaderCountdownFormatter.Format(
            snapshots,
            panelDismissAtUtc: null,
            DateTimeOffset.UtcNow);

        Assert.Equal(string.Empty, text);
    }

    private static ProjectHealthSnapshot Snapshot(DateTimeOffset? rebuildQuietUntilUtc = null) =>
        new(
            ProjectId: "proj",
            DisplayName: "Sample",
            Health: MonitorHealth.Green,
            HealthLabel: "Healthy",
            State: ProjectLifecycleState.WaitingForEdits,
            LastExitCode: null,
            LastDuration: null,
            LastErrorPreview: null,
            ErrorCount: 0,
            WarningCount: 0,
            LastChangedUtc: DateTimeOffset.UtcNow,
            LastBuildFinishedAtUtc: null,
            IsActive: true,
            ProgressSteps: [],
            RebuildQuietUntilUtc: rebuildQuietUntilUtc);
}
