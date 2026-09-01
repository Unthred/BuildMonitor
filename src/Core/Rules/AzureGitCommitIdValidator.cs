using System.Text.RegularExpressions;

namespace BuildMonitor.Core.Rules;

/// <summary>Validates Azure Builds <c>sourceVersion</c> values suitable for commit navigation.</summary>
public static partial class AzureGitCommitIdValidator
{
    [GeneratedRegex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex FullShaRegex();

    public static bool IsValidCommitId(string? sourceVersion)
    {
        if (string.IsNullOrWhiteSpace(sourceVersion))
        {
            return false;
        }

        return FullShaRegex().IsMatch(sourceVersion.Trim());
    }

    public static string? Normalize(string? sourceVersion)
    {
        if (!IsValidCommitId(sourceVersion))
        {
            return null;
        }

        return sourceVersion!.Trim().ToLowerInvariant();
    }
}
