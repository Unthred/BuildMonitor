using System.Text.Json;

namespace BuildMonitor.Infrastructure.LocalBuild;

public static class BuildIssueCountResolver
{
    public static (int Errors, int Warnings) Resolve(string buildOutput, string? existingLogPath)
    {
        if (!IncrementalBuildDetector.WasCompileSkipped(buildOutput))
        {
            return (
                BuildLogParser.ParseErrorCount(buildOutput),
                BuildLogParser.ParseWarningCount(buildOutput));
        }

        if (!string.IsNullOrWhiteSpace(existingLogPath))
        {
            var prevPath = existingLogPath + ".prev";
            if (File.Exists(prevPath))
            {
                var fromPrev = ReadCountsFromLogText(File.ReadAllText(prevPath));
                if (fromPrev.Errors > 0 || fromPrev.Warnings > 0)
                {
                    return fromPrev;
                }
            }

            var fromMeta = TryReadMetadataCounts(existingLogPath);
            if (fromMeta.Errors > 0 || fromMeta.Warnings > 0)
            {
                return fromMeta;
            }
        }

        var fromNote = BuildLogParser.TryParseIncrementalHealthNote(buildOutput);
        if (fromNote.Errors > 0 || fromNote.Warnings > 0)
        {
            return fromNote;
        }

        return (
            BuildLogParser.ParseErrorCount(buildOutput),
            BuildLogParser.ParseWarningCount(buildOutput));
    }

    /// <summary>
    /// Watch/run console output often lacks a full MSBuild summary. Avoid clearing persisted build counts
    /// when the parsed result is zero but the output does not contain a definitive build outcome.
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

        // Parsed 0/0 must never clear existing build issues from watch/run console output.
        if (currentErrors > 0 || currentWarnings > 0)
        {
            return false;
        }

        return normalizedOutput.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase);
    }

    private static (int Errors, int Warnings) ReadCountsFromLogText(string logText)
    {
        var errors = BuildLogParser.ParseErrorCount(logText);
        var warnings = BuildLogParser.ParseWarningCount(logText);
        if (errors > 0 || warnings > 0)
        {
            return (errors, warnings);
        }

        return BuildLogParser.TryParseIncrementalHealthNote(logText);
    }

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
