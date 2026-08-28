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

    [Fact]
    public void RequiresUrgentCardRebuild_false_when_only_build_source_age_ticks()
    {
        var now = new DateTimeOffset(2026, 8, 28, 6, 0, 0, TimeSpan.Zero);
        var completed = AzureRunAt(
            now.AddMinutes(-5),
            runId: 491,
            buildNumber: "20260828.3",
            state: PipelineRunState.Completed,
            result: PipelineRunResult.Succeeded,
            finishedAtUtc: now.AddMinutes(-1));
        var previous = StatusPanelPresentationBuilder.Build(
            [SnapshotWithAzure(completed, now)],
            null,
            now);
        var azureRow = Assert.Single(previous.Cards[0].BuildSourceRows!);
        var agedRow = azureRow with { AgeDisplay = "42m · 5m0s" };
        var agedCard = previous.Cards[0] with { BuildSourceRows = new[] { agedRow } };
        var current = previous with { Cards = new[] { agedCard } };

        Assert.Equal("42m · 5m0s", agedRow.AgeDisplay);
        Assert.Equal(azureRow.AzureNavigation?.Run.Uri, agedRow.AzureNavigation?.Run.Uri);
        Assert.False(StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(previous, current));
    }

    [Fact]
    public void RequiresUrgentCardRebuild_when_build_source_run_id_changes()
    {
        var now = new DateTimeOffset(2026, 8, 28, 6, 0, 0, TimeSpan.Zero);
        var started = now.AddMinutes(-4);
        var previous = StatusPanelPresentationBuilder.Build(
            [SnapshotWithAzure(AzureRunAt(started, runId: 491, buildNumber: "20260828.3"), now)],
            null,
            now);
        var current = StatusPanelPresentationBuilder.Build(
            [SnapshotWithAzure(AzureRunAt(started, runId: 492, buildNumber: "20260828.4"), now)],
            null,
            now);

        Assert.True(StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(previous, current));
    }

    private static AzurePipelineRunInfo AzureRunAt(
        DateTimeOffset startedUtc,
        long runId = 491,
        string buildNumber = "20260828.3",
        PipelineRunState state = PipelineRunState.InProgress,
        PipelineRunResult result = PipelineRunResult.Unknown,
        DateTimeOffset? finishedAtUtc = null) =>
        new(
            DefinitionId: 8,
            PipelineDisplayName: "WitherbyConnect",
            RunId: runId,
            BuildNumber: buildNumber,
            State: state,
            Result: result,
            Branch: "PR #185",
            QueuedAtUtc: startedUtc.AddMinutes(-1),
            StartedAtUtc: startedUtc,
            FinishedAtUtc: finishedAtUtc,
            RunUrl: $"https://dev.azure.com/org/project/_build/results?buildId={runId}&view=results",
            PullRequestNumber: 185);

    private static AzurePipelineRunInfo AzureRun(
        long runId = 491,
        string buildNumber = "20260828.3",
        PipelineRunState state = PipelineRunState.InProgress) =>
        AzureRunAt(DateTimeOffset.UtcNow.AddMinutes(-4), runId, buildNumber, state);

    private static ProjectHealthSnapshot SnapshotWithAzure(AzurePipelineRunInfo primary, DateTimeOffset utcNow) =>
        new(
            ProjectId: "p1",
            DisplayName: "Demo",
            Health: MonitorHealth.Amber,
            HealthLabel: "Building",
            State: ProjectLifecycleState.Idle,
            LastExitCode: 0,
            LastDuration: null,
            LastErrorPreview: null,
            ErrorCount: 0,
            WarningCount: 0,
            LastChangedUtc: utcNow,
            LastBuildFinishedAtUtc: null,
            IsActive: true,
            ProgressSteps: [],
            Azure: new ProjectAzureHealthFacet(
                AzureMonitoringAvailability.Available,
                AzureCiMonitoringState.Activity,
                "master",
                primary,
                [],
                utcNow,
                HasSelectedPipelines: true,
                NavigationContext: new AzureBuildNavigationContext(
                    "p1",
                    "conn",
                    "https://dev.azure.com/org",
                    "project",
                    "repo")));

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
