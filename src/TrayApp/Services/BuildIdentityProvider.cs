using System.IO;
using System.Reflection;

namespace BuildMonitor.TrayApp.Services;

internal static class BuildIdentityProvider
{
    private const string DeployInfoFileName = "deploy-info.txt";

    public static string FormatFooterText()
    {
        var identity = Load();

        var datePart = identity.DeployedUtc?.ToString("yyyy-MM-dd")
                       ?? identity.BuiltUtc?.ToString("yyyy-MM-dd")
                       ?? "unknown";

        var commitWithDirty = identity.IsGitDirty == true
            ? $"{identity.GitCommitShort}-dirty"
            : identity.GitCommitShort;

        // Footer text is intentionally a single line.
        return identity.Version is not null
            ? $"BuildMonitor {identity.Version} • {commitWithDirty} • {datePart}"
            : $"BuildMonitor • {commitWithDirty} • {datePart}";
    }

    private static BuildIdentity Load()
    {
        // Prefer deploy-info.txt because it records what is actually installed.
        var deployInfoPath = Path.Combine(AppContext.BaseDirectory, DeployInfoFileName);
        if (File.Exists(deployInfoPath))
        {
            var fromFile = TryLoadFromDeployInfoFile(deployInfoPath);
            if (fromFile is not null)
            {
                return fromFile;
            }
        }

        return LoadFromAssemblyMetadata();
    }

    private static BuildIdentity? TryLoadFromDeployInfoFile(string path)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines)
            {
                var idx = line.IndexOf(':');
                if (idx < 0)
                {
                    continue;
                }

                var key = line[..idx].Trim();
                var value = line[(idx + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    dict[key] = value;
                }
            }

            var version = dict.TryGetValue("Version", out var v) ? v : null;
            var commit = dict.TryGetValue("Commit", out var c) ? c : null;
            var branch = dict.TryGetValue("CommitBranch", out var b) ? b : null;

            DateTimeOffset? deployedUtc = null;
            if (dict.TryGetValue("DeployedUtc", out var d)
                && DateTimeOffset.TryParse(d, out var parsed))
            {
                deployedUtc = parsed;
            }

            DateTimeOffset? builtUtc = null;
            if (dict.TryGetValue("BuiltUtc", out var bu)
                && DateTimeOffset.TryParse(bu, out var parsedBuilt))
            {
                builtUtc = parsedBuilt;
            }

            var isDirty = dict.TryGetValue("Dirty", out var dirty) &&
                          dirty.Equals("true", StringComparison.OrdinalIgnoreCase);

            // deploy-info.txt may include "c8d-dirty" already; we still parse it into
            // commit short + dirty flag for consistency.
            if (commit is not null && commit.EndsWith("-dirty", StringComparison.OrdinalIgnoreCase))
            {
                commit = commit[..^"-dirty".Length];
                isDirty = true;
            }

            return new BuildIdentity(
                version,
                commit ?? "unknown",
                branch ?? "unknown",
                builtUtc,
                deployedUtc,
                isDirty);
        }
        catch
        {
            // Best-effort only. If parsing fails, fall back to assembly metadata.
            return null;
        }
    }

    private static BuildIdentity LoadFromAssemblyMetadata()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        var version = GetAsmMetadata(asm, "BuildMonitor.Version");
        var commit = GetAsmMetadata(asm, "BuildMonitor.GitCommit");
        var branch = GetAsmMetadata(asm, "BuildMonitor.GitBranch");
        var builtUtcText = GetAsmMetadata(asm, "BuildMonitor.BuiltUtc");
        var isDirtyText = GetAsmMetadata(asm, "BuildMonitor.IsGitDirty");

        DateTimeOffset? builtUtc = null;
        if (!string.IsNullOrWhiteSpace(builtUtcText)
            && DateTimeOffset.TryParse(builtUtcText, out var parsedBuilt))
        {
            builtUtc = parsedBuilt;
        }

        bool isDirty = !string.IsNullOrWhiteSpace(isDirtyText)
                       && isDirtyText.Equals("true", StringComparison.OrdinalIgnoreCase);

        return new BuildIdentity(
            version,
            commit ?? "unknown",
            branch ?? "unknown",
            builtUtc,
            DeployedUtc: null,
            IsGitDirty: isDirty);
    }

    private static string? GetAsmMetadata(Assembly asm, string key)
    {
        var attributes = asm.GetCustomAttributes<AssemblyMetadataAttribute>();
        foreach (var a in attributes)
        {
            if (string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return a.Value;
            }
        }

        return null;
    }

    private sealed record BuildIdentity(
        string? Version,
        string GitCommitShort,
        string GitBranch,
        DateTimeOffset? BuiltUtc,
        DateTimeOffset? DeployedUtc,
        bool IsGitDirty);
}

