using System.Text.Json;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Builds an isolated BuildMonitor project entry from a claimed parent, without
/// writing into the target repository.
/// </summary>
public static class DerivedWorktreeProjectFactory
{
    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static MonitoredProjectSettings Create(
        MonitoredProjectSettings parent,
        string worktreePath,
        string newProjectId,
        string? allocatedApplicationUrl)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newProjectId);

        if (parent.Local is null)
        {
            throw new InvalidOperationException(
                $"Parent project '{parent.Id}' has no Local attachment to derive from.");
        }

        var parentLocal = parent.Local;
        var normalizedWorktree = VerificationProviderPath.Normalize(worktreePath);
        var folderName = Path.GetFileName(normalizedWorktree);
        if (string.IsNullOrWhiteSpace(folderName))
        {
            folderName = "worktree";
        }

        var clone = DeepClone(parent);
        clone.Id = newProjectId;
        clone.DisplayName = $"{parent.DisplayName} ({folderName})";
        clone.IsActiveInSession = true;
        clone.Local = CloneLocal(parentLocal, normalizedWorktree, parent.Id, allocatedApplicationUrl);
        // Azure attachment (if any) stays — same repository association, local root differs.
        return clone;
    }

    public static string? RemapPathUnderRoot(string? path, string parentRoot, string worktreeRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (!Path.IsPathRooted(path))
        {
            return path.Replace('/', Path.DirectorySeparatorChar);
        }

        var full = Path.GetFullPath(path);
        var parentNormalized = VerificationProviderPath.Normalize(parentRoot);
        if (!full.StartsWith(parentNormalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !VerificationProviderPath.EqualsExact(full, parentNormalized))
        {
            return full;
        }

        var relative = Path.GetRelativePath(parentNormalized, full);
        return Path.GetFullPath(Path.Combine(worktreeRoot, relative));
    }

    private static LocalProjectAttachment CloneLocal(
        LocalProjectAttachment parentLocal,
        string worktreeRoot,
        string parentProjectId,
        string? allocatedApplicationUrl)
    {
        var parentRoot = VerificationProviderPath.Normalize(parentLocal.RootFolder);
        return new LocalProjectAttachment
        {
            RootFolder = worktreeRoot,
            ProjectFile = RemapPathUnderRoot(parentLocal.ProjectFile, parentRoot, worktreeRoot)
                ?? string.Empty,
            LaunchProfile = parentLocal.LaunchProfile,
            ExtraDotNetArgs = parentLocal.ExtraDotNetArgs,
            ApplicationUrl = allocatedApplicationUrl?.Trim() ?? string.Empty,
            TestProjectFile = RemapPathUnderRoot(parentLocal.TestProjectFile, parentRoot, worktreeRoot)
                ?? string.Empty,
            StartOnLaunch = true,
            BuildControlMode = parentLocal.BuildControlMode,
            PreferredSiteUrlScheme = parentLocal.PreferredSiteUrlScheme,
            DerivedFromProjectId = parentProjectId,
            RunOptions = DeepCloneRunOptions(parentLocal.RunOptions)
        };
    }

    private static MonitoredProjectSettings DeepClone(MonitoredProjectSettings source)
    {
        var json = JsonSerializer.Serialize(source, CloneOptions);
        return JsonSerializer.Deserialize<MonitoredProjectSettings>(json, CloneOptions)
            ?? throw new InvalidOperationException("Failed to clone parent project settings.");
    }

    private static ProjectRunOptions DeepCloneRunOptions(ProjectRunOptions source)
    {
        var json = JsonSerializer.Serialize(source, CloneOptions);
        return JsonSerializer.Deserialize<ProjectRunOptions>(json, CloneOptions) ?? new ProjectRunOptions();
    }
}
