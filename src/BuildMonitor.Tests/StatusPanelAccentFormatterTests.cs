using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class StatusPanelAccentFormatterTests
{
    [Fact]
    public void FormatActivityLabel_uses_active_restore_step()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.Building,
            progressSteps:
            [
                new BuildProgressStep("Restore packages", BuildStepStatus.Active)
            ]);

        Assert.Equal("Restoring", StatusPanelAccentFormatter.FormatActivityLabel(snapshot));
    }

    [Fact]
    public void FormatActivityLabel_prefers_building_over_restarting()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.Building,
            progressSteps: [new BuildProgressStep("Restore packages", BuildStepStatus.Active)],
            isRestarting: true);

        Assert.Equal("Restoring", StatusPanelAccentFormatter.FormatActivityLabel(snapshot));
    }

    [Fact]
    public void FormatActivityLabel_uses_active_project_step()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.Building,
            progressSteps:
            [
                new BuildProgressStep("Restore packages", BuildStepStatus.Complete),
                new BuildProgressStep("VesselCompliance.Web", BuildStepStatus.Active)
            ]);

        Assert.Equal("Compiling VesselComplian…", StatusPanelAccentFormatter.FormatActivityLabel(snapshot));
    }

    [Fact]
    public void ResolveAccentHealth_uses_warning_count_during_build()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.Building,
            health: MonitorHealth.Green,
            warningCount: 2000);

        Assert.Equal(MonitorHealth.Amber, StatusPanelAccentFormatter.ResolveAccentHealth(snapshot));
    }

    [Fact]
    public void ShouldShowAccentRail_when_starting_site()
    {
        var snapshot = Snapshot(
            ProjectLifecycleState.Watching,
            listenUrl: "http://localhost:5154",
            listenUrlReady: false,
            supportsRestart: true);

        Assert.True(StatusPanelAccentFormatter.ShouldShowAccentRail(snapshot));
        Assert.Equal("Starting site", StatusPanelAccentFormatter.FormatActivityLabel(snapshot));
    }

    private static ProjectHealthSnapshot Snapshot(
        ProjectLifecycleState state,
        MonitorHealth health = MonitorHealth.Green,
        int errorCount = 0,
        int warningCount = 0,
        IReadOnlyList<BuildProgressStep>? progressSteps = null,
        bool isRestarting = false,
        string? listenUrl = null,
        bool listenUrlReady = false,
        bool supportsRestart = false) =>
        new(
            "p1",
            "Demo",
            health,
            "Success",
            state,
            0,
            null,
            null,
            errorCount,
            warningCount,
            DateTimeOffset.UtcNow,
            null,
            true,
            progressSteps ?? [],
            listenUrl,
            listenUrlReady,
            supportsRestart,
            IsRestarting: isRestarting);
}
