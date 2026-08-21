using BuildMonitor.Core.Settings;

namespace BuildMonitor.Infrastructure.LocalBuild;

public enum AssociateLocalOutcome
{
    Created,
    NoCandidates,
    Cancelled,
    Invalid,
    AlreadyHasLocal
}

public sealed record AssociateLocalResult(
    AssociateLocalOutcome Outcome,
    LocalProjectAttachment? Local,
    string? Error);

/// <summary>
/// Builds a validation-complete Local attachment for an Azure-only project.
/// Never returns a RootFolder-only attachment.
/// </summary>
public static class AssociateLocalAttachmentBuilder
{
    /// <param name="pickWhenMultiple">
    /// Invoked when multiple candidates exist. Return a relative path from the list, or null to cancel.
    /// </param>
    public static AssociateLocalResult TryBuild(
        MonitoredProjectSettings project,
        string rootFolder,
        Func<IReadOnlyList<string>, string?>? pickWhenMultiple = null)
    {
        if (project.Local is not null)
        {
            return new AssociateLocalResult(AssociateLocalOutcome.AlreadyHasLocal, null, "Project already has a Local attachment.");
        }

        if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
        {
            return new AssociateLocalResult(
                AssociateLocalOutcome.Invalid,
                null,
                "Selected folder does not exist.");
        }

        var candidates = LocalProjectCandidateDiscovery.DiscoverRelativeCandidates(rootFolder);
        if (candidates.Count == 0)
        {
            return new AssociateLocalResult(
                AssociateLocalOutcome.NoCandidates,
                null,
                "No .csproj or .sln was found under that folder. Azure-only project was left unchanged.");
        }

        string? relative;
        if (candidates.Count == 1)
        {
            relative = candidates[0];
        }
        else
        {
            if (pickWhenMultiple is null)
            {
                return new AssociateLocalResult(
                    AssociateLocalOutcome.Cancelled,
                    null,
                    "Multiple projects found; selection was cancelled.");
            }

            relative = pickWhenMultiple(candidates);
            if (string.IsNullOrWhiteSpace(relative))
            {
                return new AssociateLocalResult(AssociateLocalOutcome.Cancelled, null, null);
            }

            if (!candidates.Contains(relative, StringComparer.OrdinalIgnoreCase))
            {
                // Allow absolute path under root → normalise
                var asRelative = LocalProjectCandidateDiscovery.ToRelative(rootFolder, relative);
                if (!candidates.Contains(asRelative, StringComparer.OrdinalIgnoreCase))
                {
                    return new AssociateLocalResult(
                        AssociateLocalOutcome.Invalid,
                        null,
                        "Selected project file is not one of the discovered candidates.");
                }

                relative = asRelative;
            }
        }

        var local = new LocalProjectAttachment
        {
            RootFolder = Path.GetFullPath(rootFolder),
            ProjectFile = relative
        };

        var validationErrors = ValidateLocal(local);
        if (validationErrors.Count > 0)
        {
            return new AssociateLocalResult(
                AssociateLocalOutcome.Invalid,
                null,
                string.Join(" ", validationErrors));
        }

        return new AssociateLocalResult(AssociateLocalOutcome.Created, local, null);
    }

    /// <summary>Applies a successful build onto the project (in-memory). Preserves Azure.</summary>
    public static bool TryApply(MonitoredProjectSettings project, AssociateLocalResult result, out string? error)
    {
        error = null;
        if (result.Outcome != AssociateLocalOutcome.Created || result.Local is null)
        {
            error = result.Error ?? "Local association was not created.";
            return false;
        }

        if (project.Local is not null)
        {
            error = "Project already has a Local attachment.";
            return false;
        }

        project.Local = result.Local;
        if (string.IsNullOrWhiteSpace(project.DisplayName)
            || project.DisplayName.Equals("Azure project", StringComparison.OrdinalIgnoreCase))
        {
            project.DisplayName = Path.GetFileNameWithoutExtension(result.Local.ProjectFile);
        }

        return true;
    }

    private static List<string> ValidateLocal(LocalProjectAttachment local)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(local.RootFolder) || !Directory.Exists(local.RootFolder))
        {
            errors.Add("RootFolder must exist.");
        }

        if (string.IsNullOrWhiteSpace(local.ProjectFile))
        {
            errors.Add("ProjectFile (.csproj/.sln) is required.");
        }
        else if (!string.IsNullOrWhiteSpace(local.RootFolder))
        {
            var full = Path.IsPathRooted(local.ProjectFile)
                ? local.ProjectFile
                : Path.Combine(local.RootFolder, local.ProjectFile);
            if (!File.Exists(full))
            {
                errors.Add($"ProjectFile not found at {full}.");
            }
        }

        return errors;
    }
}
