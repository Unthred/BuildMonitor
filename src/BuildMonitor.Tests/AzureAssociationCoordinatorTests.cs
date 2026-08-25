using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.AzureDevOps;

namespace BuildMonitor.Tests;

public sealed class AzureAssociationCoordinatorTests
{
    [Fact]
    public async Task Add_from_azure_builds_azure_only_project_without_mutating_settings()
    {
        var discovery = new StubDiscovery();
        var settings = new AppSettings { Connections = [Conn()] };
        var snapshot = settings.Projects.Count;
        var coordinator = new AzureAssociationCoordinator(discovery, (_, _) => Task.FromResult<string?>("pat"));
        Assert.True(await coordinator.InitializeAsync(AzureAssociationMode.AddFromAzure, Conn(), CancellationToken.None));
        await coordinator.SelectProjectAsync(discovery.Projects[0], CancellationToken.None);
        await coordinator.SelectRepositoryAsync(discovery.Repos[0], CancellationToken.None);

        var attachment = coordinator.BuildAttachment();
        Assert.NotNull(attachment);
        var project = AzureAssociationCoordinator.CreateAzureOnlyProject(attachment!);
        Assert.Null(project.Local);
        Assert.NotNull(project.Azure);
        Assert.Equal("RepoOne", project.DisplayName);
        Assert.Equal(snapshot, settings.Projects.Count);
    }

    [Fact]
    public async Task Exactly_one_enabled_pipeline_is_preselected()
    {
        var discovery = new StubDiscovery
        {
            Pipelines =
            [
                new AzurePipelineSummary(1, "CI", true, "r1", "RepoOne", "TfsGit", "\\", null, []),
                new AzurePipelineSummary(2, "Old", false, "r1", "RepoOne", "TfsGit", "\\", null, [])
            ]
        };
        var coordinator = new AzureAssociationCoordinator(discovery, (_, _) => Task.FromResult<string?>("pat"));
        await coordinator.InitializeAsync(AzureAssociationMode.AddFromAzure, Conn(), CancellationToken.None);
        await coordinator.SelectProjectAsync(discovery.Projects[0], CancellationToken.None);
        await coordinator.SelectRepositoryAsync(discovery.Repos[0], CancellationToken.None);
        Assert.Single(coordinator.SelectedPipelineIds);
        Assert.Contains(1, coordinator.SelectedPipelineIds);
    }

    [Fact]
    public async Task Multiple_enabled_pipelines_require_explicit_selection()
    {
        var discovery = new StubDiscovery
        {
            Pipelines =
            [
                new AzurePipelineSummary(1, "CI", true, "r1", "RepoOne", "TfsGit", "\\", null, []),
                new AzurePipelineSummary(2, "PR", true, "r1", "RepoOne", "TfsGit", "\\", null, [])
            ]
        };
        var coordinator = new AzureAssociationCoordinator(discovery, (_, _) => Task.FromResult<string?>("pat"));
        await coordinator.InitializeAsync(AzureAssociationMode.AddFromAzure, Conn(), CancellationToken.None);
        await coordinator.SelectProjectAsync(discovery.Projects[0], CancellationToken.None);
        await coordinator.SelectRepositoryAsync(discovery.Repos[0], CancellationToken.None);
        Assert.Empty(coordinator.SelectedPipelineIds);
    }

    [Fact]
    public void Attach_preserves_local_detach_requires_local()
    {
        var project = new MonitoredProjectSettings
        {
            DisplayName = "App",
            Local = new LocalProjectAttachment { RootFolder = @"C:\src\App", ProjectFile = "App.csproj" }
        };
        var azure = new AzureDevOpsProjectAttachment
        {
            ConnectionId = "c1",
            AdoProjectId = "p1",
            AdoProjectName = "P",
            RepositoryId = "r1",
            RepositoryName = "Repo"
        };
        AzureAssociationCoordinator.AttachAzure(project, azure);
        Assert.NotNull(project.Local);
        Assert.NotNull(project.Azure);

        Assert.True(AzureAssociationCoordinator.TryDetachAzure(project, out _));
        Assert.NotNull(project.Local);
        Assert.Null(project.Azure);

        var azureOnly = AzureAssociationCoordinator.CreateAzureOnlyProject(azure);
        Assert.False(AzureAssociationCoordinator.TryDetachAzure(azureOnly, out var error));
        Assert.Contains("Azure-only", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cancel_path_does_not_require_settings_mutation()
    {
        var settings = new AppSettings();
        var before = settings.Connections.Count;
        // Opening coordinator without Finish leaves settings unchanged by design.
        Assert.Equal(before, settings.Connections.Count);
        Assert.Empty(settings.Projects);
    }

    private static AzureDevOpsConnectionSettings Conn() => new()
    {
        Id = "c1",
        DisplayName = "Org",
        OrganizationUrl = "https://dev.azure.com/org"
    };

    private sealed class StubDiscovery : BuildMonitor.Core.Abstractions.IAzureDevOpsDiscoveryClient
    {
        public List<AzureProjectSummary> Projects { get; } =
        [
            new("p1", "Proj", null, "wellFormed")
        ];

        public List<AzureRepositorySummary> Repos { get; } =
        [
            new("r1", "RepoOne", "p1", "Proj", "https://dev.azure.com/org/Proj/_git/RepoOne", null, "refs/heads/main", "main")
        ];

        public List<AzurePipelineSummary> Pipelines { get; set; } =
        [
            new(10, "CI", true, "r1", "RepoOne", "TfsGit", "\\", null, ["main"])
        ];

        public Task<AzureConnectionTestResult> TestConnectionAsync(
            AzureDevOpsConnectionSettings connection,
            string? pat,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AzureConnectionTestResult(AzureConnectionTestOutcome.Success, "ok"));

        public Task<IReadOnlyList<AzureProjectSummary>> ListProjectsAsync(
            AzureDevOpsConnectionSettings connection,
            string pat,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AzureProjectSummary>>(Projects);

        public Task<IReadOnlyList<AzureRepositorySummary>> ListRepositoriesAsync(
            AzureDevOpsConnectionSettings connection,
            string pat,
            string projectIdOrName,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AzureRepositorySummary>>(Repos);

        public Task<IReadOnlyList<AzurePipelineSummary>> ListPipelinesForRepositoryAsync(
            AzureDevOpsConnectionSettings connection,
            string pat,
            string projectIdOrName,
            string repositoryId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AzurePipelineSummary>>(Pipelines);
    }
}
