namespace BuildMonitor.Core.Settings;

public static class AppSettingsValidator
{
    public static IReadOnlyList<string> Validate(AppSettings settings)
    {
        var errors = new List<string>();

        foreach (var project in settings.Projects)
        {
            if (string.IsNullOrWhiteSpace(project.DisplayName))
            {
                errors.Add($"Project {project.Id}: DisplayName is required.");
            }

            if (string.IsNullOrWhiteSpace(project.RootFolder) || !Directory.Exists(project.RootFolder))
            {
                errors.Add($"Project {project.DisplayName}: RootFolder must exist.");
            }

            if (string.IsNullOrWhiteSpace(project.ProjectFile))
            {
                errors.Add($"Project {project.DisplayName}: ProjectFile (.csproj/.sln) is required.");
            }
            else if (!string.IsNullOrWhiteSpace(project.RootFolder))
            {
                var full = Path.IsPathRooted(project.ProjectFile)
                    ? project.ProjectFile
                    : Path.Combine(project.RootFolder, project.ProjectFile);
                if (!File.Exists(full))
                {
                    errors.Add($"Project {project.DisplayName}: ProjectFile not found at {full}.");
                }
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
}
