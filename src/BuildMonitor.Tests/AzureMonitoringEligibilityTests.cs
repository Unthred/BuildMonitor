using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.AzureDevOps;

namespace BuildMonitor.Tests;

public sealed class AzureMonitoringEligibilityTests
{
    [Fact]
    public void Zero_pipelines_not_eligible()
    {
        var settings = new AppSettings
        {
            Connections =
            [
                new AzureDevOpsConnectionSettings
                {
                    Id = "c1",
                    OrganizationUrl = "https://dev.azure.com/org"
                }
            ],
            Projects =
            [
                new MonitoredProjectSettings
                {
                    Id = "p1",
                    DisplayName = "P",
                    IsActiveInSession = true,
                    Azure = new AzureDevOpsProjectAttachment
                    {
                        ConnectionId = "c1",
                        AdoProjectName = "proj",
                        RepositoryName = "repo",
                        Pipelines = []
                    }
                }
            ]
        };

        Assert.Empty(AzureMonitoringService.GetEligibleProjects(settings));
    }

    [Fact]
    public void Inactive_session_not_eligible()
    {
        var settings = new AppSettings
        {
            Connections =
            [
                new AzureDevOpsConnectionSettings
                {
                    Id = "c1",
                    OrganizationUrl = "https://dev.azure.com/org"
                }
            ],
            Projects =
            [
                new MonitoredProjectSettings
                {
                    Id = "p1",
                    DisplayName = "P",
                    IsActiveInSession = false,
                    Azure = new AzureDevOpsProjectAttachment
                    {
                        ConnectionId = "c1",
                        AdoProjectName = "proj",
                        RepositoryName = "repo",
                        Pipelines =
                        [
                            new AzurePipelineSelection { DefinitionId = 8, DisplayName = "CI" }
                        ]
                    }
                }
            ]
        };

        Assert.Empty(AzureMonitoringService.GetEligibleProjects(settings));
    }

    [Fact]
    public void Active_with_pipeline_and_connection_is_eligible()
    {
        var settings = new AppSettings
        {
            Connections =
            [
                new AzureDevOpsConnectionSettings
                {
                    Id = "c1",
                    OrganizationUrl = "https://dev.azure.com/org"
                }
            ],
            Projects =
            [
                new MonitoredProjectSettings
                {
                    Id = "p1",
                    DisplayName = "P",
                    IsActiveInSession = true,
                    Azure = new AzureDevOpsProjectAttachment
                    {
                        ConnectionId = "c1",
                        AdoProjectName = "proj",
                        RepositoryName = "repo",
                        Pipelines =
                        [
                            new AzurePipelineSelection { DefinitionId = 8, DisplayName = "CI" }
                        ]
                    }
                }
            ]
        };

        Assert.Single(AzureMonitoringService.GetEligibleProjects(settings));
    }
}
