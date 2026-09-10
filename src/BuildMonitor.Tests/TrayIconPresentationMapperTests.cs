using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class TrayIconPresentationMapperTests
{
    [Fact]
    public void No_active_projects_returns_Neutral_idle()
    {
        var p = TrayIconPresentationMapper.Resolve([]);
        Assert.Equal(TrayHealthRing.Neutral, p.Health);
        Assert.False(p.IsActive);
        Assert.False(p.IsAnimatable);
    }

    [Fact]
    public void All_healthy_idle_returns_Healthy_static()
    {
        var snapshots = new[]
        {
            Local("p1", ProjectLifecycleState.Watching, MonitorHealth.Green),
            Local("p2", ProjectLifecycleState.Running, MonitorHealth.Green)
        };

        var p = TrayIconPresentationMapper.Resolve(snapshots);
        Assert.Equal(TrayHealthRing.Healthy, p.Health);
        Assert.False(p.IsActive);
    }

    [Fact]
    public void Healthy_Local_build_is_animated_green()
    {
        var p = TrayIconPresentationMapper.Resolve(
            [Local("p1", ProjectLifecycleState.Building, MonitorHealth.Green)]);
        Assert.Equal(TrayHealthRing.Healthy, p.Health);
        Assert.True(p.IsActive);
        Assert.True(p.IsAnimatable);
    }

    [Theory]
    [InlineData(ProjectLifecycleState.Testing)]
    [InlineData(ProjectLifecycleState.WaitingForEdits)]
    public void Healthy_Local_busy_states_are_animated(ProjectLifecycleState state)
    {
        var p = TrayIconPresentationMapper.Resolve([Local("p1", state, MonitorHealth.Green)]);
        Assert.Equal(TrayHealthRing.Healthy, p.Health);
        Assert.True(p.IsAnimatable);
    }

    [Fact]
    public void Healthy_restarting_is_animated()
    {
        var p = TrayIconPresentationMapper.Resolve(
            [Local("p1", ProjectLifecycleState.Watching, MonitorHealth.Green, isRestarting: true)]);
        Assert.True(p.IsAnimatable);
        Assert.Equal(TrayHealthRing.Healthy, p.Health);
    }

    [Fact]
    public void Healthy_Azure_active_is_animated()
    {
        var snapshots = new[]
        {
            Local("p1", ProjectLifecycleState.Watching, MonitorHealth.Green),
            AzureOnly("p2", PipelineRunState.InProgress, AzureCiMonitoringState.Activity)
        };

        var p = TrayIconPresentationMapper.Resolve(snapshots);
        Assert.Equal(TrayHealthRing.Healthy, p.Health);
        Assert.True(p.IsAnimatable);
    }

    [Fact]
    public void Attention_idle_is_amber_static()
    {
        var p = TrayIconPresentationMapper.Resolve(
            [Local("p1", ProjectLifecycleState.Watching, MonitorHealth.Amber, warningCount: 3)]);
        Assert.Equal(TrayHealthRing.Attention, p.Health);
        Assert.False(p.IsActive);
    }

    [Fact]
    public void Attention_active_is_amber_animated()
    {
        var p = TrayIconPresentationMapper.Resolve(
            [Local("p1", ProjectLifecycleState.Testing, MonitorHealth.Amber, warningCount: 2)]);
        Assert.Equal(TrayHealthRing.Attention, p.Health);
        Assert.True(p.IsAnimatable);
    }

    [Fact]
    public void Azure_auth_required_without_activity_returns_Attention_idle()
    {
        var facet = AzureFacet(PipelineRunState.Completed, AzureCiMonitoringState.NotMonitored)
            with { Availability = AzureMonitoringAvailability.AuthRequired };
        var snapshots = new[]
        {
            Local("p1", ProjectLifecycleState.Watching, MonitorHealth.Amber) with { Azure = facet }
        };

        var p = TrayIconPresentationMapper.Resolve(snapshots);
        Assert.Equal(TrayHealthRing.Attention, p.Health);
        Assert.False(p.IsActive);
    }

    [Fact]
    public void Failed_idle_is_red_static()
    {
        var p = TrayIconPresentationMapper.Resolve(
            [Local("p1", ProjectLifecycleState.BuildFailed, MonitorHealth.Red)]);
        Assert.Equal(TrayHealthRing.Failed, p.Health);
        Assert.False(p.IsAnimatable);
    }

    [Fact]
    public void Failed_plus_Local_build_remains_red_static()
    {
        var snapshots = new[]
        {
            Local("p1", ProjectLifecycleState.BuildFailed, MonitorHealth.Red),
            Local("p2", ProjectLifecycleState.Building, MonitorHealth.Amber)
        };

        var p = TrayIconPresentationMapper.Resolve(snapshots);
        Assert.Equal(TrayHealthRing.Failed, p.Health);
        Assert.True(p.IsActive);
        Assert.False(p.IsAnimatable);
    }

    [Fact]
    public void Failed_plus_Azure_build_remains_red_static()
    {
        var snapshots = new[]
        {
            Local("p1", ProjectLifecycleState.BuildFailed, MonitorHealth.Red),
            AzureOnly("p2", PipelineRunState.InProgress, AzureCiMonitoringState.Activity)
        };

        var p = TrayIconPresentationMapper.Resolve(snapshots);
        Assert.Equal(TrayHealthRing.Failed, p.Health);
        Assert.True(p.IsActive);
        Assert.False(p.IsAnimatable);
    }

    [Fact]
    public void Multi_project_worst_health_wins_Failed()
    {
        var snapshots = new[]
        {
            Local("p1", ProjectLifecycleState.Watching, MonitorHealth.Green),
            Local("p2", ProjectLifecycleState.Building, MonitorHealth.Amber),
            Local("p3", ProjectLifecycleState.BuildFailed, MonitorHealth.Red)
        };

        var p = TrayIconPresentationMapper.Resolve(snapshots);
        Assert.Equal(TrayHealthRing.Failed, p.Health);
        Assert.False(p.IsAnimatable);
    }

    [Fact]
    public void Simultaneous_Local_and_Azure_activity_is_one_active_flag()
    {
        var snapshots = new[]
        {
            Local("p1", ProjectLifecycleState.Building, MonitorHealth.Amber, warningCount: 1),
            AzureOnly("p2", PipelineRunState.InProgress, AzureCiMonitoringState.Activity)
        };

        var p = TrayIconPresentationMapper.Resolve(snapshots);
        Assert.Equal(TrayHealthRing.Attention, p.Health);
        Assert.True(p.IsActive);
        Assert.True(p.IsAnimatable);
    }

    [Fact]
    public void Unknown_rollup_without_activity_returns_Neutral()
    {
        var p = TrayIconPresentationMapper.Resolve(
            [Local("p1", ProjectLifecycleState.Watching, MonitorHealth.Unknown)]);
        Assert.Equal(TrayHealthRing.Neutral, p.Health);
        Assert.False(p.IsActive);
    }

    [Fact]
    public void Neutral_with_activity_is_grey_animated()
    {
        var p = TrayIconPresentationMapper.Resolve(
            [Local("p1", ProjectLifecycleState.Building, MonitorHealth.Unknown)]);
        Assert.Equal(TrayHealthRing.Neutral, p.Health);
        Assert.True(p.IsAnimatable);
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
