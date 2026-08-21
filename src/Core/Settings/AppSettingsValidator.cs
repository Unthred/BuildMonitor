namespace BuildMonitor.Core.Settings;

public static class AppSettingsValidator
{
    public static IReadOnlyList<string> Validate(AppSettings settings)
    {
        var errors = new List<string>();
        var connectionIds = settings.Connections
            .Select(c => c.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var connection in settings.Connections)
        {
            if (string.IsNullOrWhiteSpace(connection.Id))
            {
                errors.Add("Azure DevOps connection Id is required.");
            }

            if (string.IsNullOrWhiteSpace(connection.OrganizationUrl)
                || !Uri.TryCreate(connection.OrganizationUrl, UriKind.Absolute, out var orgUri)
                || (orgUri.Scheme != Uri.UriSchemeHttp && orgUri.Scheme != Uri.UriSchemeHttps))
            {
                var label = string.IsNullOrWhiteSpace(connection.DisplayName) ? connection.Id : connection.DisplayName;
                errors.Add($"Azure DevOps connection '{label}': OrganizationUrl must be an absolute http(s) URL.");
            }
        }

        foreach (var project in settings.Projects)
        {
            if (string.IsNullOrWhiteSpace(project.DisplayName))
            {
                errors.Add($"Project {project.Id}: DisplayName is required.");
            }

            if (project.Local is null && project.Azure is null)
            {
                errors.Add($"Project {project.DisplayName}: at least one of Local or Azure attachment is required.");
                continue;
            }

            if (project.Local is not null)
            {
                ValidateLocal(project.DisplayName, project.Local, errors);
            }

            if (project.Azure is not null)
            {
                ValidateAzure(project.DisplayName, project.Azure, connectionIds, errors);
            }
        }

        if (settings.Monitor.HealthRefreshSeconds < 2)
        {
            errors.Add("Monitor.HealthRefreshSeconds must be >= 2.");
        }

        if (settings.Monitor.MaxConcurrentActiveProjects < 1)
        {
            errors.Add("Monitor.MaxConcurrentActiveProjects must be >= 1.");
        }

        if (settings.Monitor.ControlPlanePort is < 1024 or > 65535)
        {
            errors.Add("Monitor.ControlPlanePort must be between 1024 and 65535.");
        }

        if (settings.Monitor.ControlPlaneBusyTimeoutSeconds is < 30 or > 3600)
        {
            errors.Add("Monitor.ControlPlaneBusyTimeoutSeconds must be between 30 and 3600.");
        }

        if (settings.AppBehavior.ToastDurationSeconds is < 2 or > 120)
        {
            errors.Add("AppBehavior.ToastDurationSeconds must be between 2 and 120.");
        }

        return errors;
    }

    private static void ValidateLocal(string displayName, LocalProjectAttachment local, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(local.RootFolder) || !Directory.Exists(local.RootFolder))
        {
            errors.Add($"Project {displayName}: RootFolder must exist.");
        }

        if (string.IsNullOrWhiteSpace(local.ProjectFile))
        {
            errors.Add($"Project {displayName}: ProjectFile (.csproj/.sln) is required.");
        }
        else if (!string.IsNullOrWhiteSpace(local.RootFolder))
        {
            var full = Path.IsPathRooted(local.ProjectFile)
                ? local.ProjectFile
                : Path.Combine(local.RootFolder, local.ProjectFile);
            if (!File.Exists(full))
            {
                errors.Add($"Project {displayName}: ProjectFile not found at {full}.");
            }
        }
    }

    private static void ValidateAzure(
        string displayName,
        AzureDevOpsProjectAttachment azure,
        HashSet<string> connectionIds,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(azure.ConnectionId) || !connectionIds.Contains(azure.ConnectionId))
        {
            errors.Add($"Project {displayName}: Azure attachment ConnectionId must reference a configured connection.");
        }

        if (string.IsNullOrWhiteSpace(azure.AdoProjectId) && string.IsNullOrWhiteSpace(azure.AdoProjectName))
        {
            errors.Add($"Project {displayName}: Azure attachment requires an ADO project id or name.");
        }

        if (string.IsNullOrWhiteSpace(azure.RepositoryId) && string.IsNullOrWhiteSpace(azure.RepositoryName))
        {
            errors.Add($"Project {displayName}: Azure attachment requires a repository id or name.");
        }

        foreach (var pipeline in azure.Pipelines)
        {
            if (pipeline.DefinitionId <= 0)
            {
                errors.Add($"Project {displayName}: Azure pipeline DefinitionId must be > 0.");
            }
        }
    }
}
