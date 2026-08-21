namespace BuildMonitor.Core.Rules;

/// <summary>Normalizes Azure Git branch refs to short names (presentation / settings).</summary>
public static class AzureGitBranchNormalizer
{
    public static string? ToShortName(string? branchRefOrName)
    {
        if (string.IsNullOrWhiteSpace(branchRefOrName))
        {
            return null;
        }

        var value = branchRefOrName.Trim();
        const string headsPrefix = "refs/heads/";
        if (value.StartsWith(headsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return value[headsPrefix.Length..];
        }

        return value;
    }
}
