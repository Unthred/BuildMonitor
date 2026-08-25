using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class ProjectHealthComposerTests
{
    [Fact]
    public void Merge_azure_red_beats_local_green()
    {
        var azure = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Failed,
            "master",
            null,
            [],
            DateTimeOffset.UtcNow);

        Assert.Equal(MonitorHealth.Red, ProjectHealthComposer.Merge(MonitorHealth.Green, azure));
    }

    [Fact]
    public void Merge_auth_required_is_amber()
    {
        var azure = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.AuthRequired,
            AzureCiMonitoringState.NotMonitored,
            null,
            null,
            [],
            DateTimeOffset.UtcNow,
            HasSelectedPipelines: true);

        Assert.Equal(MonitorHealth.Amber, ProjectHealthComposer.Merge(MonitorHealth.Green, azure));
    }

    [Fact]
    public void Merge_not_monitored_leaves_local()
    {
        var azure = AzureFacetComposer.NotMonitored(DateTimeOffset.UtcNow);
        Assert.Equal(MonitorHealth.Green, ProjectHealthComposer.Merge(MonitorHealth.Green, azure));
    }

    [Fact]
    public void WithAzure_updates_snapshot_health()
    {
        var local = new ProjectHealthSnapshot(
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

        var azure = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Activity,
            "feature/x",
            null,
            [],
            DateTimeOffset.UtcNow);

        var merged = ProjectHealthComposer.WithAzure(local, azure);
        Assert.Equal(MonitorHealth.Amber, merged.Health);
        Assert.NotNull(merged.Azure);
    }
}
