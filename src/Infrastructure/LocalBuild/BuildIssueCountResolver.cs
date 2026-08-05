using System.Text.Json;

namespace BuildMonitor.Infrastructure.LocalBuild;

public static class BuildIssueCountResolver
{
    /// <summary>
    /// Returns error/warning counts from the current build output only.
    /// Tray, log store, and log viewer must all use this so numbers stay in sync.
    /// </summary>
    public static (int Errors, int Warnings) Resolve(string buildOutput, string? existingLogPath = null)
    {
        _ = existingLogPath;
        return (
            BuildLogParser.ParseErrorCount(buildOutput),
            BuildLogParser.ParseWarningCount(buildOutput));
    }

    /// <summary>
    /// Watch/run console output often lacks a full MSBuild summary. Avoid clearing persisted build counts
    /// when the parsed result is zero but the output does not contain a definitive build outcome.
    /// When MSBuild reports Build succeeded / FAILED (with or without a 0/0 summary), apply the counts.
    /// </summary>
    public static bool ShouldApplyWatchOutputCounts(
        string normalizedOutput,
        int currentErrors,
        int currentWarnings,
        int parsedErrors,
        int parsedWarnings)
    {
        if (parsedErrors == currentErrors && parsedWarnings == currentWarnings)
        {
            return false;
        }

        if (parsedErrors > 0 || parsedWarnings > 0)
        {
            return true;
        }

        if (HasDefinitiveBuildOutcome(normalizedOutput))
        {
            return true;
        }

        // Parsed 0/0 with no build outcome must not clear existing counts from host/run noise.
        if (currentErrors > 0 || currentWarnings > 0)
        {
            return false;
        }

        return false;
    }

    public static bool HasDefinitiveBuildOutcome(string logText) =>
        !string.IsNullOrWhiteSpace(logText)
        && (logText.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase)
            || logText.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
            || logText.Contains("Build failed", StringComparison.OrdinalIgnoreCase));

    public static (int Errors, int Warnings) ReadPersistedMetadataCounts(string? logFilePath) =>
        string.IsNullOrWhiteSpace(logFilePath) ? (0, 0) : TryReadMetadataCounts(logFilePath);

    private static (int Errors, int Warnings) TryReadMetadataCounts(string logFilePath)
    {
        var metaPath = ResolveMetadataPath(logFilePath);
        if (metaPath is null || !File.Exists(metaPath))
        {
            return (0, 0);
        }

        try
        {
            var json = File.ReadAllText(metaPath);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var errors = root.TryGetProperty("ErrorCount", out var errorNode)
                ? errorNode.GetInt32()
                : 0;
            var warnings = root.TryGetProperty("WarningCount", out var warningNode)
                ? warningNode.GetInt32()
                : 0;
            return (errors, warnings);
        }
        catch
        {
            return (0, 0);
        }
    }

    internal static string? ResolveMetadataPath(string? logFilePath)
    {
        if (string.IsNullOrWhiteSpace(logFilePath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(logFilePath) ?? string.Empty;
        var fileName = Path.GetFileName(logFilePath);
        if (fileName.EndsWith(".log.prev", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName[..^5];
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        return Path.Combine(directory, stem + ".meta.json");
    }
}
