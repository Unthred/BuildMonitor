using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class BuildSourceLocalStatusPresentationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Settled_build_ok_shows_succeeded_and_elapsed_age()
    {
        var row = BuildLocalRow(ProjectLifecycleState.BuildOk, isRestarting: false);

        Assert.Equal("✓", row.StatusGlyph);
        Assert.Equal("Succeeded", row.StatusText);
        Assert.Equal(StatusPanelRowEmphasis.Success, row.Emphasis);
        Assert.NotEqual("In progress", row.AgeDisplay);
    }

    [Fact]
    public void Explicit_rebuild_building_shows_building_status_and_in_progress_age()
    {
        var row = BuildLocalRow(
            ProjectLifecycleState.Building,
            isRestarting: false,
            progressSteps:
            [
                new BuildProgressStep("Restore", BuildStepStatus.Complete),
                new BuildProgressStep("Compile", BuildStepStatus.Active)
            ]);

        Assert.Equal("◉", row.StatusGlyph);
        Assert.StartsWith("Building", row.StatusText, StringComparison.Ordinal);
        Assert.Equal("In progress", row.AgeDisplay);
        Assert.Equal(StatusPanelRowEmphasis.Active, row.Emphasis);
    }

    [Fact]
    public void Restart_phase_shows_restarting_before_building_semantics()
    {
        var row = BuildLocalRow(ProjectLifecycleState.BuildOk, isRestarting: true);

        Assert.Equal("◉", row.StatusGlyph);
        Assert.Equal("Restarting", row.StatusText);
        Assert.Equal(StatusPanelRowEmphasis.Active, row.Emphasis);
    }

    [Fact]
    public void Build_failed_shows_failed_status()
    {
        var row = BuildLocalRow(ProjectLifecycleState.BuildFailed, isRestarting: false, lastExitCode: 1);

        Assert.Equal("✕", row.StatusGlyph);
        Assert.Equal("Build failed", row.StatusText);
        Assert.Equal(StatusPanelRowEmphasis.Error, row.Emphasis);
    }

    private static BuildSourcePresentationRow BuildLocalRow(
        ProjectLifecycleState state,
        bool isRestarting,
        int lastExitCode = 0,
        IReadOnlyList<BuildProgressStep>? progressSteps = null)
    {
        var controlPlane = ControlPlaneStatusFormatter.Format(
            new ProjectHealthSnapshot(
                ProjectId: "p1",
                DisplayName: "Demo",
                Health: MonitorHealth.Green,
                HealthLabel: "Healthy",
                State: state,
                LastExitCode: lastExitCode,
                LastDuration: TimeSpan.FromSeconds(12),
                LastErrorPreview: null,
                ErrorCount: 0,
                WarningCount: 0,
                LastChangedUtc: Now,
                LastBuildFinishedAtUtc: Now.AddMinutes(-2),
                IsActive: true,
                ProgressSteps: progressSteps ?? [],
                SupportsAppRestart: true,
                IsRestarting: isRestarting),
            Now);

        var snapshot = new ProjectHealthSnapshot(
            ProjectId: "p1",
            DisplayName: "Demo",
            Health: MonitorHealth.Green,
            HealthLabel: "Healthy",
            State: state,
            LastExitCode: lastExitCode,
            LastDuration: TimeSpan.FromSeconds(12),
            LastErrorPreview: null,
            ErrorCount: 0,
            WarningCount: 0,
            LastChangedUtc: Now,
            LastBuildFinishedAtUtc: Now.AddMinutes(-2),
            IsActive: true,
            ProgressSteps: progressSteps ?? [],
            SupportsAppRestart: true,
            IsRestarting: isRestarting);

        var row = BuildSourcePresentationBuilder.TryBuildLocal(snapshot, controlPlane, Now);
        Assert.NotNull(row);
        return row!;
    }
}
