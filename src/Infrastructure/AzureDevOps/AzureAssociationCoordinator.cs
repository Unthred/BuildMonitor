using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Infrastructure.AzureDevOps;

public enum AzureAssociationMode
{
    AddFromAzure,
    AttachToExisting,
    ChangeExisting
}

/// <summary>Testable Add/Attach/Change Azure association flow (no WPF types).</summary>
public sealed class AzureAssociationCoordinator
{
    private readonly IAzureDevOpsDiscoveryClient discoveryClient;
    private readonly Func<string, CancellationToken, Task<string?>> loadPatAsync;
    private readonly ILocalGitContextReader? gitContextReader;

    public AzureAssociationCoordinator(
        IAzureDevOpsDiscoveryClient discoveryClient,
        Func<string, CancellationToken, Task<string?>> loadPatAsync,
        ILocalGitContextReader? gitContextReader = null)
    {
        this.discoveryClient = discoveryClient;
        this.loadPatAsync = loadPatAsync;
        this.gitContextReader = gitContextReader;
    }

    public AzureAssociationMode Mode { get; private set; }
    public AzureDevOpsConnectionSettings? Connection { get; private set; }
    public string? StatusMessage { get; private set; }
    public bool IsBusy { get; private set; }

    public IReadOnlyList<AzureProjectSummary> Projects { get; private set; } = [];
    public IReadOnlyList<AzureRepositorySummary> Repositories { get; private set; } = [];
    public IReadOnlyList<AzurePipelineCandidate> Pipelines { get; private set; } = [];

    public AzureProjectSummary? SelectedProject { get; private set; }
    public AzureRepositorySummary? SelectedRepository { get; private set; }
    public HashSet<int> SelectedPipelineIds { get; } = [];

    public AzureRepositoryMatchSuggester.Suggestion? SuggestedMatch { get; private set; }
    public LocalGitContext? LocalGitContext { get; private set; }

    public async Task<bool> InitializeAsync(
        AzureAssociationMode mode,
        AzureDevOpsConnectionSettings connection,
        CancellationToken cancellationToken,
        string? localRootFolder = null,
        AzureDevOpsProjectAttachment? existingAzure = null)
    {
        Mode = mode;
        Connection = connection;
        StatusMessage = null;
        SuggestedMatch = null;
        LocalGitContext = null;
        Projects = [];
        Repositories = [];
        Pipelines = [];
        SelectedProject = null;
        SelectedRepository = null;
        SelectedPipelineIds.Clear();

        var pat = await loadPatAsync(connection.Id, cancellationToken);
        if (string.IsNullOrWhiteSpace(pat))
        {
            StatusMessage = "No PAT is stored for the Azure connection. Configure it on the Azure tab first.";
            return false;
        }

        IsBusy = true;
        try
        {
            Projects = await discoveryClient.ListProjectsAsync(connection, pat, cancellationToken);
            if (Projects.Count == 0)
            {
                StatusMessage = "No Azure DevOps projects were returned for this connection.";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(localRootFolder) && gitContextReader is not null)
            {
                LocalGitContext = await gitContextReader.ReadAsync(localRootFolder, cancellationToken);
            }

            if (existingAzure is not null)
            {
                SelectedProject = Projects.FirstOrDefault(p =>
                    string.Equals(p.Id, existingAzure.AdoProjectId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.Name, existingAzure.AdoProjectName, StringComparison.OrdinalIgnoreCase));
                if (SelectedProject is not null)
                {
                    await SelectProjectAsync(SelectedProject, cancellationToken);
                    SelectedRepository = Repositories.FirstOrDefault(r =>
                        string.Equals(r.Id, existingAzure.RepositoryId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(r.Name, existingAzure.RepositoryName, StringComparison.OrdinalIgnoreCase));
                    if (SelectedRepository is not null)
                    {
                        await SelectRepositoryAsync(SelectedRepository, cancellationToken);
                        foreach (var pipe in existingAzure.Pipelines)
                        {
                            SelectedPipelineIds.Add(pipe.DefinitionId);
                        }
                    }
                }
            }

            return true;
        }
        catch (AzureDevOpsDiscoveryException ex)
        {
            StatusMessage = ex.Message;
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = "Azure discovery failed: " + ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SelectProjectAsync(AzureProjectSummary project, CancellationToken cancellationToken)
    {
        SelectedProject = project;
        SelectedRepository = null;
        SelectedPipelineIds.Clear();
        Pipelines = [];
        Repositories = [];
        SuggestedMatch = null;
        StatusMessage = null;

        if (Connection is null)
        {
            return;
        }

        var pat = await loadPatAsync(Connection.Id, cancellationToken);
        if (string.IsNullOrWhiteSpace(pat))
        {
            StatusMessage = "No PAT is stored for the Azure connection.";
            return;
        }

        IsBusy = true;
        try
        {
            Repositories = await discoveryClient.ListRepositoriesAsync(
                Connection,
                pat,
                project.Id,
                cancellationToken);

            if (Repositories.Count == 0)
            {
                StatusMessage = "No Git repositories were found in this Azure project.";
            }
            else if (LocalGitContext is not null)
            {
                SuggestedMatch = AzureRepositoryMatchSuggester.Suggest(
                    LocalGitContext.Remotes,
                    Projects,
                    Repositories);
            }
        }
        catch (AzureDevOpsDiscoveryException ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SelectRepositoryAsync(AzureRepositorySummary repository, CancellationToken cancellationToken)
    {
        SelectedRepository = repository;
        SelectedPipelineIds.Clear();
        Pipelines = [];
        StatusMessage = null;

        if (Connection is null || SelectedProject is null)
        {
            return;
        }

        var pat = await loadPatAsync(Connection.Id, cancellationToken);
        if (string.IsNullOrWhiteSpace(pat))
        {
            StatusMessage = "No PAT is stored for the Azure connection.";
            return;
        }

        IsBusy = true;
        try
        {
            var discovered = await discoveryClient.ListPipelinesForRepositoryAsync(
                Connection,
                pat,
                SelectedProject.Id,
                repository.Id,
                cancellationToken);

            Pipelines = discovered
                .Select(p => new AzurePipelineCandidate(p.DefinitionId, p.DisplayName, p.IsEnabled, p.TriggerBranches))
                .ToArray();

            ApplyDefaultPipelineSelection();

            if (Pipelines.Count == 0)
            {
                StatusMessage = "No build definitions were returned for this repository (Connected / Not monitored if you finish with zero pipelines).";
            }
        }
        catch (AzureDevOpsDiscoveryException ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetPipelineSelected(int definitionId, bool selected)
    {
        if (selected)
        {
            SelectedPipelineIds.Add(definitionId);
        }
        else
        {
            SelectedPipelineIds.Remove(definitionId);
        }
    }

    public void ApplySuggestedRepository()
    {
        if (SuggestedMatch is null)
        {
            return;
        }

        var repo = Repositories.FirstOrDefault(r =>
            string.Equals(r.Id, SuggestedMatch.RepositoryId, StringComparison.OrdinalIgnoreCase));
        if (repo is not null)
        {
            // Fire and forget pattern avoided — caller awaits SelectRepositoryAsync after setting project.
            SelectedRepository = repo;
        }
    }

    /// <summary>Builds attachment from current selection. Does not mutate settings.</summary>
    public AzureDevOpsProjectAttachment? BuildAttachment()
    {
        if (Connection is null || SelectedProject is null || SelectedRepository is null)
        {
            StatusMessage = "Select an Azure project and repository.";
            return null;
        }

        var pipelines = Pipelines
            .Where(p => SelectedPipelineIds.Contains(p.DefinitionId))
            .Select(p => new AzurePipelineSelection
            {
                DefinitionId = p.DefinitionId,
                DisplayName = p.DisplayName,
                IncludedBranches = p.TriggerBranches.ToList()
            })
            .ToList();

        return new AzureDevOpsProjectAttachment
        {
            ConnectionId = Connection.Id,
            AdoProjectId = SelectedProject.Id,
            AdoProjectName = SelectedProject.Name,
            RepositoryId = SelectedRepository.Id,
            RepositoryName = SelectedRepository.Name,
            RepositoryRemoteUrl = SelectedRepository.RemoteUrl ?? SelectedRepository.WebUrl,
            DefaultBranch = SelectedRepository.DefaultBranchShortName,
            Pipelines = pipelines
        };
    }

    public static MonitoredProjectSettings CreateAzureOnlyProject(AzureDevOpsProjectAttachment azure)
    {
        return new MonitoredProjectSettings
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = string.IsNullOrWhiteSpace(azure.RepositoryName) ? "Azure project" : azure.RepositoryName,
            IsActiveInSession = false,
            Local = null,
            Azure = CloneAttachment(azure)
        };
    }

    public static void AttachAzure(MonitoredProjectSettings project, AzureDevOpsProjectAttachment azure)
    {
        project.Azure = CloneAttachment(azure);
    }

    public static void ChangeAzure(MonitoredProjectSettings project, AzureDevOpsProjectAttachment azure)
    {
        project.Azure = CloneAttachment(azure);
    }

    public static bool TryDetachAzure(MonitoredProjectSettings project, out string? error)
    {
        error = null;
        if (project.Azure is null)
        {
            error = "This project has no Azure attachment.";
            return false;
        }

        if (project.Local is null)
        {
            error = "Cannot detach Azure from an Azure-only project. Remove the project instead, or associate a local folder first.";
            return false;
        }

        project.Azure = null;
        return true;
    }

    private void ApplyDefaultPipelineSelection()
    {
        SelectedPipelineIds.Clear();
        var enabled = Pipelines.Where(p => p.IsEnabled).ToArray();
        if (enabled.Length == 1)
        {
            SelectedPipelineIds.Add(enabled[0].DefinitionId);
        }
    }

    private static AzureDevOpsProjectAttachment CloneAttachment(AzureDevOpsProjectAttachment azure) =>
        new()
        {
            ConnectionId = azure.ConnectionId,
            AdoProjectId = azure.AdoProjectId,
            AdoProjectName = azure.AdoProjectName,
            RepositoryId = azure.RepositoryId,
            RepositoryName = azure.RepositoryName,
            RepositoryRemoteUrl = azure.RepositoryRemoteUrl,
            DefaultBranch = azure.DefaultBranch,
            ExtraWatchedBranches = [.. azure.ExtraWatchedBranches],
            Pipelines = azure.Pipelines.Select(p => new AzurePipelineSelection
            {
                DefinitionId = p.DefinitionId,
                DisplayName = p.DisplayName,
                IncludedBranches = [.. p.IncludedBranches],
                NotificationMode = p.NotificationMode,
                Priority = p.Priority
            }).ToList()
        };
}

public sealed record AzurePipelineCandidate(
    int DefinitionId,
    string DisplayName,
    bool IsEnabled,
    IReadOnlyList<string> TriggerBranches);
