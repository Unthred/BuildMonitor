using BuildMonitor.Core.Settings;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Branches used for presentation focus and explicit watched-branch attention.
/// Pipeline current health uses newest active/completed run for the selected pipeline
/// (not this set), so a newer PR failure is not hidden behind an older default-branch success.
/// </summary>
public static class AzureRelevantBranchSet
{
    public static IReadOnlyList<string> Build(
        AzureDevOpsProjectAttachment azure,
        string? focusBranchShort,
        int? pipelineDefinitionId = null)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Add(set, azure.DefaultBranch);
        Add(set, focusBranchShort);

        foreach (var branch in azure.ExtraWatchedBranches)
        {
            Add(set, branch);
        }

        foreach (var pipeline in azure.Pipelines)
        {
            if (pipelineDefinitionId is not null && pipeline.DefinitionId != pipelineDefinitionId.Value)
            {
                continue;
            }

            foreach (var branch in pipeline.IncludedBranches)
            {
                Add(set, branch);
            }
        }

        return set.ToList();
    }

    private static void Add(HashSet<string> set, string? branch)
    {
        var shortName = AzureGitBranchNormalizer.ToShortName(branch);
        if (!string.IsNullOrWhiteSpace(shortName))
        {
            set.Add(shortName);
        }
    }
}
