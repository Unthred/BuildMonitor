using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class StatusPanelIdleRailFormatterTests
{
    [Fact]
    public void FormatIdleLabel_healthy_when_green_and_web_ready()
    {
        Assert.Equal("Healthy", StatusPanelIdleRailFormatter.FormatIdleLabel(MonitorHealth.Green, webReady: true));
    }

    [Fact]
    public void FormatIdleLabel_healthy_when_green_without_web()
    {
        Assert.Equal("Healthy", StatusPanelIdleRailFormatter.FormatIdleLabel(MonitorHealth.Green, webReady: false));
    }

    [Fact]
    public void FormatIdleLabel_amber_is_attention_not_warnings()
    {
        Assert.Equal("Attention", StatusPanelIdleRailFormatter.FormatIdleLabel(MonitorHealth.Amber, webReady: false));
        Assert.NotEqual("Warnings", StatusPanelIdleRailFormatter.FormatIdleLabel(MonitorHealth.Amber, webReady: false));
    }

    [Fact]
    public void ResolveHealth_rollup_worst_active_project()
    {
        var snapshots = new[]
        {
            Snapshot("a", MonitorHealth.Green),
            Snapshot("b", MonitorHealth.Red)
        };

        Assert.Equal(MonitorHealth.Red, StatusPanelIdleRailFormatter.ResolveHealth(snapshots));
    }

    private static ProjectHealthSnapshot Snapshot(string projectId, MonitorHealth health) =>
        new(
            ProjectId: projectId,
            DisplayName: projectId,
            Health: health,
            HealthLabel: health.ToString(),
            State: ProjectLifecycleState.Running,
            LastExitCode: 0,
            LastDuration: TimeSpan.FromSeconds(1),
            LastErrorPreview: null,
            ErrorCount: 0,
            WarningCount: 0,
            LastChangedUtc: DateTimeOffset.UtcNow,
            LastBuildFinishedAtUtc: DateTimeOffset.UtcNow,
            IsActive: true,
            ProgressSteps: []);
}
