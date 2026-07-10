using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class StatusPanelCardUiChangeDetectorTests
{
    [Fact]
    public void RequiresCardRebuild_false_when_ui_fields_unchanged()
    {
        var snapshot = SampleSnapshot();
        var previous = new[] { snapshot };
        var current = new[] { snapshot with { LastChangedUtc = snapshot.LastChangedUtc.AddSeconds(5) } };

        Assert.False(StatusPanelCardUiChangeDetector.RequiresCardRebuild(previous, current));
    }

    [Fact]
    public void RequiresCardRebuild_true_when_error_count_changes()
    {
        var previous = new[] { SampleSnapshot(errorCount: 0) };
        var current = new[] { SampleSnapshot(errorCount: 2) };

        Assert.True(StatusPanelCardUiChangeDetector.RequiresCardRebuild(previous, current));
    }

    [Fact]
    public void RequiresCardRebuild_true_when_progress_step_status_changes()
    {
        var previous = new[]
        {
            SampleSnapshot(progressSteps: [new BuildProgressStep("Restore packages", BuildStepStatus.Active)])
        };
        var current = new[]
        {
            SampleSnapshot(progressSteps: [new BuildProgressStep("Restore packages", BuildStepStatus.Complete)])
        };

        Assert.True(StatusPanelCardUiChangeDetector.RequiresCardRebuild(previous, current));
    }

    [Fact]
    public void RequiresCardRebuild_true_when_active_project_set_changes()
    {
        var previous = new[] { SampleSnapshot(projectId: "a") };
        var current = new[] { SampleSnapshot(projectId: "b") };

        Assert.True(StatusPanelCardUiChangeDetector.RequiresCardRebuild(previous, current));
    }

    private static ProjectHealthSnapshot SampleSnapshot(
        string projectId = "proj",
        int errorCount = 0,
        IReadOnlyList<BuildProgressStep>? progressSteps = null) =>
        new(
            ProjectId: projectId,
            DisplayName: "Sample",
            Health: MonitorHealth.Green,
            HealthLabel: "Healthy",
            State: ProjectLifecycleState.Running,
            LastExitCode: 0,
            LastDuration: TimeSpan.FromSeconds(3),
            LastErrorPreview: null,
            ErrorCount: errorCount,
            WarningCount: 0,
            LastChangedUtc: DateTimeOffset.UtcNow,
            LastBuildFinishedAtUtc: DateTimeOffset.UtcNow,
            IsActive: true,
            ProgressSteps: progressSteps ?? [],
            ListenUrl: "http://localhost:5000",
            ListenUrlReady: true,
            SupportsAppRestart: true);
}
