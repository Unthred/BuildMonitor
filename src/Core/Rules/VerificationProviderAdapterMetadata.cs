namespace BuildMonitor.Core.Rules;

public sealed record VerificationProviderAdapterMetadata(
    string Name,
    bool VerificationProvider,
    string ContractVersion,
    string AdapterVersion,
    string AdapterSource,
    string AdapterSourcePath);

public static class VerificationProviderAdapterMetadataParser
{
    public const string ContractVersion = "1";
    public const string AdapterVersion = "1.0.0";
    public const string AdapterSource = "Unthred/BuildMonitor";
    public const string AdapterSourcePath = "docs/ops/agent-skills/buildmonitor-control-plane";

    public static VerificationProviderAdapterMetadata Parse(string skillMarkdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillMarkdown);
        var frontMatter = ExtractFrontMatter(skillMarkdown);
        return new VerificationProviderAdapterMetadata(
            Name: ReadScalar(frontMatter, "name") ?? string.Empty,
            VerificationProvider: string.Equals(ReadScalar(frontMatter, "verificationProvider"), "true", StringComparison.OrdinalIgnoreCase),
            ContractVersion: ReadScalar(frontMatter, "verificationProviderContractVersion") ?? string.Empty,
            AdapterVersion: ReadScalar(frontMatter, "adapterVersion") ?? string.Empty,
            AdapterSource: ReadScalar(frontMatter, "adapterSource") ?? string.Empty,
            AdapterSourcePath: ReadScalar(frontMatter, "adapterSourcePath") ?? string.Empty);
    }

    public static bool MatchesCanonical(VerificationProviderAdapterMetadata metadata) =>
        string.Equals(metadata.Name, VerificationProviderClaim.AdapterSkillName, StringComparison.Ordinal)
        && metadata.VerificationProvider
        && string.Equals(metadata.ContractVersion, ContractVersion, StringComparison.Ordinal)
        && string.Equals(metadata.AdapterVersion, AdapterVersion, StringComparison.Ordinal)
        && string.Equals(metadata.AdapterSource, AdapterSource, StringComparison.Ordinal)
        && string.Equals(metadata.AdapterSourcePath, AdapterSourcePath, StringComparison.Ordinal);

    internal static string ExtractFrontMatter(string text)
    {
        var span = text.AsSpan().TrimStart();
        if (!span.StartsWith("---", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var rest = span[3..];
        var end = rest.IndexOf("\n---", StringComparison.Ordinal);
        if (end < 0)
        {
            return string.Empty;
        }

        return rest[..end].ToString();
    }

    private static string? ReadScalar(string frontMatter, string key)
    {
        using var reader = new StringReader(frontMatter);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(key + ":", StringComparison.Ordinal))
            {
                var value = trimmed[(key.Length + 1)..].Trim().Trim('"').Trim('\'');
                return value;
            }
        }

        return null;
    }
}
