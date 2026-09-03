using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class TrayIconPresentationMapperTests
{
    [Fact]
    public void No_active_projects_returns_Neutral() =>
        Assert.Equal(
            TrayIconPresentationState.Neutral,
            TrayIconPresentationMapper.Resolve([]));

    [Fact]
    public void All_healthy_returns_Healthy()
    {
        var snapshots = new[]
        {
            Local("p1", ProjectLifecycleState.Watching, MonitorHealth.Green),
            Local("p2", ProjectLifecycleState.Running, MonitorHealth.Green)
        };

        Assert.Equal(TrayIconPresentationState.Healthy, TrayIconPresentationMapper.Resolve(snapshots));
    }

    [Fact]
    public void Local_building_returns_Building()
    {
        var snapshots = new[] { Local("p1", ProjectLifecycleState.Building, MonitorHealth.Amber) };
        Assert.Equal(TrayIconPresentationState.Building, TrayIconPresentationMapper.Resolve(snapshots));
    }

    [Theory]
    [InlineData(ProjectLifecycleState.Testing)]
    [InlineData(ProjectLifecycleState.WaitingForEdits)]
    public void Local_busy_states_return_Building(ProjectLifecycleState state)
    {
        var snapshots = new[] { Local("p1", state, MonitorHealth.Amber) };
        Assert.Equal(TrayIconPresentationState.Building, TrayIconPresentationMapper.Resolve(snapshots));
    }

    [Fact]
    public void Local_restarting_returns_Building()
    {
        var snapshots = new[]
        {
            Local("p1", ProjectLifecycleState.Watching, MonitorHealth.Green, isRestarting: true)
        };

        Assert.Equal(TrayIconPresentationState.Building, TrayIconPresentationMapper.Resolve(snapshots));
    }

    [Fact]
    public void Azure_building_with_healthy_local_returns_Building()
    {
        var snapshots = new[]
        {
            Local("p1", ProjectLifecycleState.Watching, MonitorHealth.Green),
            AzureOnly("p2", PipelineRunState.InProgress, AzureCiMonitoringState.Activity)
        };

        Assert.Equal(TrayIconPresentationState.Building, TrayIconPresentationMapper.Resolve(snapshots));
    }

    [Fact]
    public void Amber_warning_without_activity_returns_Attention()
    {
        var snapshots = new[]
        {
            Local("p1", ProjectLifecycleState.Watching, MonitorHealth.Amber, warningCount: 3)
        };

        Assert.Equal(TrayIconPresentationState.Attention, TrayIconPresentationMapper.Resolve(snapshots));
    }

    [Fact]
    public void Azure_auth_required_without_activity_returns_Attention()
    {
        var facet = AzureFacet(PipelineRunState.Completed, AzureCiMonitoringState.NotMonitored)
            with { Availability = AzureMonitoringAvailability.AuthRequired };
        var snapshots = new[]
        {
            Local("p1", ProjectLifecycleState.Watching, MonitorHealth.Amber) with { Azure = facet }
        };

        Assert.Equal(TrayIconPresentationState.Attention, TrayIconPresentationMapper.Resolve(snapshots));
    }

    [Fact]
    public void Failure_returns_Failed()
    {
        var snapshots = new[] { Local("p1", ProjectLifecycleState.BuildFailed, MonitorHealth.Red) };
        Assert.Equal(TrayIconPresentationState.Failed, TrayIconPresentationMapper.Resolve(snapshots));
    }

    [Fact]
    public void Failure_plus_local_build_returns_Failed()
    {
        var snapshots = new[]
        {
            Local("p1", ProjectLifecycleState.BuildFailed, MonitorHealth.Red),
            Local("p2", ProjectLifecycleState.Building, MonitorHealth.Amber)
        };

        Assert.Equal(TrayIconPresentationState.Failed, TrayIconPresentationMapper.Resolve(snapshots));
    }

    [Fact]
    public void Failure_plus_Azure_build_returns_Failed()
    {
        var snapshots = new[]
        {
            Local("p1", ProjectLifecycleState.BuildFailed, MonitorHealth.Red),
            AzureOnly("p2", PipelineRunState.InProgress, AzureCiMonitoringState.Activity)
        };

        Assert.Equal(TrayIconPresentationState.Failed, TrayIconPresentationMapper.Resolve(snapshots));
    }

    [Fact]
    public void Multi_project_worst_state_wins_Failed_over_Building()
    {
        var snapshots = new[]
        {
            Local("p1", ProjectLifecycleState.Watching, MonitorHealth.Green),
            Local("p2", ProjectLifecycleState.Building, MonitorHealth.Amber),
            Local("p3", ProjectLifecycleState.BuildFailed, MonitorHealth.Red)
        };

        Assert.Equal(TrayIconPresentationState.Failed, TrayIconPresentationMapper.Resolve(snapshots));
    }

    [Fact]
    public void Multi_project_Building_beats_Attention()
    {
        var snapshots = new[]
        {
            Local("p1", ProjectLifecycleState.Watching, MonitorHealth.Amber, warningCount: 2),
            Local("p2", ProjectLifecycleState.Testing, MonitorHealth.Amber)
        };

        Assert.Equal(TrayIconPresentationState.Building, TrayIconPresentationMapper.Resolve(snapshots));
    }

    [Fact]
    public void Unknown_rollup_without_activity_returns_Neutral()
    {
        var snapshots = new[]
        {
            Local("p1", ProjectLifecycleState.Watching, MonitorHealth.Unknown)
        };

        Assert.Equal(TrayIconPresentationState.Neutral, TrayIconPresentationMapper.Resolve(snapshots));
    }

    private static ProjectHealthSnapshot Local(
        string id,
        ProjectLifecycleState state,
        MonitorHealth health,
        int warningCount = 0,
        bool isRestarting = false) =>
        new(
            id,
            id,
            health,
            ProjectHealthEvaluator.ToLabel(health),
            state,
            null,
            null,
            null,
            0,
            warningCount,
            DateTimeOffset.UtcNow,
            null,
            true,
            [],
            IsRestarting: isRestarting);

    private static ProjectHealthSnapshot AzureOnly(
        string id,
        PipelineRunState runState,
        AzureCiMonitoringState ciState) =>
        new(
            id,
            id,
            MonitorHealth.Green,
            "Healthy",
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
            Azure: AzureFacet(runState, ciState));

    private static ProjectAzureHealthFacet AzureFacet(
        PipelineRunState runState,
        AzureCiMonitoringState ciState) =>
        new(
            AzureMonitoringAvailability.Available,
            ciState,
            FocusBranch: "master",
            PrimaryRun: new AzurePipelineRunInfo(
                DefinitionId: 1,
                PipelineDisplayName: "CI",
                RunId: 42,
                BuildNumber: "1",
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
                RunUrl: "https://example.test/build/42"),
            AttentionRuns: [],
            PolledAtUtc: DateTimeOffset.UtcNow,
            HasSelectedPipelines: true);
}
