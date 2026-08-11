using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class StatusPanelPresentationChangeDetectorUrgentTests
{
    [Fact]
    public void RequiresUrgentCardRebuild_when_still_editing_button_appears()
    {
        var now = DateTimeOffset.UtcNow;
        var previous = StatusPanelPresentationBuilder.Build(
            [Snapshot(ProjectLifecycleState.Watching)],
            null,
            now);
        var current = StatusPanelPresentationBuilder.Build(
            [Snapshot(ProjectLifecycleState.WaitingForEdits, rebuildQuietUntilUtc: now.AddSeconds(5))],
            null,
            now);

        Assert.True(StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(previous, current));
    }

    [Fact]
    public void RequiresUrgentCardRebuild_false_when_only_countdown_ticks()
    {
        var now = DateTimeOffset.UtcNow;
        var first = StatusPanelPresentationBuilder.Build(
            [Snapshot(ProjectLifecycleState.WaitingForEdits, rebuildQuietUntilUtc: now.AddSeconds(8))],
            null,
            now);
        var second = StatusPanelPresentationBuilder.Build(
            [Snapshot(ProjectLifecycleState.WaitingForEdits, rebuildQuietUntilUtc: now.AddSeconds(8))],
            null,
            now.AddSeconds(1));

        Assert.False(StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(first, second));
    }

    [Fact]
    public void RequiresUrgentCardRebuild_when_lifecycle_moves_waiting_to_building()
    {
        var now = DateTimeOffset.UtcNow;
        var previous = StatusPanelPresentationBuilder.Build(
            [Snapshot(ProjectLifecycleState.WaitingForEdits, rebuildQuietUntilUtc: now.AddSeconds(5))],
            null,
            now);
        var current = StatusPanelPresentationBuilder.Build(
            [Snapshot(ProjectLifecycleState.Building)],
            null,
            now);

        Assert.True(StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(previous, current));
    }

    [Fact]
    public void RequiresUrgentCardRebuild_when_progress_steps_advance()
    {
        var now = DateTimeOffset.UtcNow;
        var previous = StatusPanelPresentationBuilder.Build(
            [Snapshot(
                ProjectLifecycleState.Building,
                progressSteps: [new BuildProgressStep("Restore packages", BuildStepStatus.Active)])],
            null,
            now);
        var current = StatusPanelPresentationBuilder.Build(
            [Snapshot(
                ProjectLifecycleState.Building,
                progressSteps:
                [
                    new BuildProgressStep("Restore packages", BuildStepStatus.Complete),
                    new BuildProgressStep("WitherbyConnect", BuildStepStatus.Complete)
                ])],
            null,
            now);

        Assert.True(StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(previous, current));
    }

    private static ProjectHealthSnapshot Snapshot(
        ProjectLifecycleState state,
        DateTimeOffset? rebuildQuietUntilUtc = null,
        IReadOnlyList<BuildProgressStep>? progressSteps = null) =>
        new(
            ProjectId: "p1",
            DisplayName: "Demo",
            Health: MonitorHealth.Green,
            HealthLabel: "Healthy",
            State: state,
            LastExitCode: 0,
            LastDuration: TimeSpan.FromSeconds(1),
            LastErrorPreview: null,
            ErrorCount: 0,
            WarningCount: 0,
            LastChangedUtc: DateTimeOffset.UtcNow,
            LastBuildFinishedAtUtc: DateTimeOffset.UtcNow,
            IsActive: true,
            ProgressSteps: progressSteps ?? [],
            RebuildQuietUntilUtc: rebuildQuietUntilUtc);
}
