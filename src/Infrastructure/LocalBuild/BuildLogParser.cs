using System.Text.RegularExpressions;

namespace BuildMonitor.Infrastructure.LocalBuild;

public sealed record LogIssue(int LineNumber, string Text, bool IsError);

public static class BuildLogParser
{
    private static readonly Regex ClassicWarningSummaryRegex = new(
        @"(\d+)\s+Warning\(s\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ClassicErrorSummaryRegex = new(
        @"(\d+)\s+Error\(s\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TerminalWarningCountRegex = new(
        @"\b(\d+)\s+warning\(s\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TerminalErrorCountRegex = new(
        @"\b(\d+)\s+error\(s\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CompilerErrorRegex = new(
        @"\berror\s+(CS|MSB|NU|BC|SA|IDE|CA|FS|VB|AD|SYSLIB|NETSDK|CS)\d+\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IncrementalHealthWarningRegex = new(
        @"(\d+)\s+warning\(s\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IncrementalHealthErrorRegex = new(
        @"(\d+)\s+error\(s\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CompilerWarningRegex = new(
        @"\bwarning\s+(CS|MSB|NU|BC|SA|IDE|CA|FS|VB|AD|SYSLIB|NETSDK|CS)\d+\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] ErrorMarkers =
    [
        ": error ",
        ": error:",
        "error :",
        "): error",
        "error CS",
        "error MSB",
        "error NU",
        "Build FAILED",
        "Test Run Failed"
    ];

    private static readonly string[] WarningMarkers =
    [
        ": warning ",
        ": warning:",
        "warning CS",
        "warning MSB"
    ];

    private static readonly string[] OutputLockMarkers =
    [
        "error MSB3021",
        "error MSB3027",
        "error MSB3026",
        "error CS2012",
        "being used by another process",
        "The process cannot access the file because it is being used by another process"
    ];

    public static string DeduplicateConsecutiveLines(string logText)
    {
        if (string.IsNullOrEmpty(logText))
        {
            return string.Empty;
        }

        var lines = logText.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>(lines.Length);
        string? previous = null;

        foreach (var line in lines)
        {
            if (!string.Equals(line, previous, StringComparison.Ordinal))
            {
                kept.Add(line);
            }

            previous = line;
        }

        return string.Join(Environment.NewLine, kept);
    }

    public static (int ErrorCount, IReadOnlyList<string> ErrorLines) ParseErrors(string logText)
    {
        var issues = ParseIssues(logText);
        var errors = issues.Where(i => i.IsError).Select(i => i.Text).Take(20).ToList();
        return (ParseErrorCount(logText), errors);
    }

    public static int ParseWarningCount(string logText)
    {
        var segment = ExtractLatestBuildResultSegment(logText);
        var summary = ParseBuildSummaryCount(segment, warnings: true);
        // Explicit MSBuild summary (including 0) wins — do not let a BuildMonitor
        // incremental note override "0 Warning(s)".
        if (summary >= 0)
        {
            return summary;
        }

        var fromIssues = ParseIssues(segment).Count(i => !i.IsError);
        if (fromIssues > 0)
        {
            return fromIssues;
        }

        return TryParseIncrementalHealthNote(logText).Warnings;
    }

    public static int ParseErrorCount(string logText)
    {
        var segment = ExtractLatestBuildResultSegment(logText);
        var summary = ParseBuildSummaryCount(segment, warnings: false);
        // Explicit MSBuild summary (including 0) wins — do not let a BuildMonitor
        // incremental note override "0 Error(s)".
        if (summary >= 0)
        {
            return summary;
        }

        var fromIssues = ParseIssues(segment)
            .Where(i => i.IsError)
            .Select(i => i.Text)
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (fromIssues > 0)
        {
            return fromIssues;
        }

        return TryParseIncrementalHealthNote(logText).Errors;
    }

    /// <summary>Reads resolved tray-health counts from a BuildMonitor incremental-build note line.</summary>
    public static (int Errors, int Warnings) TryParseIncrementalHealthNote(string logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return (0, 0);
        }

        var marker = "Tray health uses";
        var index = logText.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return (0, 0);
        }

        var note = logText[index..];
        var warnings = ParseLastMatchCount(note, IncrementalHealthWarningRegex);
        var errors = ParseLastMatchCount(note, IncrementalHealthErrorRegex);
        return (Math.Max(0, errors), Math.Max(0, warnings));
    }

    /// <summary>
    /// Returns compiler issues from the current log only (no carry-forward from previous builds).
    /// </summary>
    public static IReadOnlyList<LogIssue> ResolveBuildIssues(string logText, string? logFilePath)
    {
        _ = logFilePath;
        return ParseIssues(logText);
    }

    internal static IEnumerable<string> CandidatePreviousLogPaths(string? logFilePath)
    {
        if (string.IsNullOrWhiteSpace(logFilePath))
        {
            yield break;
        }

        yield return logFilePath;

        var previous = logFilePath + ".prev";
        if (!string.Equals(previous, logFilePath, StringComparison.OrdinalIgnoreCase))
        {
            yield return previous;
        }
    }

    public static bool IsOutputLockError(string logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return false;
        }

        return OutputLockMarkers.Any(marker =>
            logText.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Limits parsing to the most recent MSBuild result in accumulated dotnet watch output.
    /// When BuildMonitor banners are present, uses the latest build block so errors above
    /// <c>Build FAILED</c> are not dropped from count/issue parsing.
    /// </summary>
    internal static string ExtractLatestBuildResultSegment(string logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return string.Empty;
        }

        var normalized = logText.Replace("\r\n", "\n");
        const string buildBanner = "[BuildMonitor] ===== Build #";
        var lastBanner = normalized.LastIndexOf(buildBanner, StringComparison.Ordinal);
        if (lastBanner >= 0)
        {
            return normalized[lastBanner..];
        }

        var lastSucceeded = normalized.LastIndexOf("Build succeeded", StringComparison.OrdinalIgnoreCase);
        var lastFailed = Math.Max(
            normalized.LastIndexOf("Build FAILED", StringComparison.Ordinal),
            normalized.LastIndexOf("Build failed with", StringComparison.OrdinalIgnoreCase));
        var lastFailedSentence = normalized.LastIndexOf("The build failed", StringComparison.OrdinalIgnoreCase);
        // Watch host appends "The build failed" after MSBuild. Prefer the MSBuild
        // "Build FAILED" block so diagnostics and the error summary are not dropped.
        var start = Math.Max(lastSucceeded, lastFailed);
        if (lastFailedSentence > start)
        {
            start = lastFailed >= 0 ? lastFailed : lastFailedSentence;
        }

        if (start < 0)
        {
            return normalized;
        }

        if (lastFailed >= 0 && start == lastFailed)
        {
            // MSBuild prints diagnostics before the Build FAILED line — include them.
            var lineStart = lastFailed > 0
                ? normalized.LastIndexOf('\n', lastFailed - 1)
                : -1;
            var searchFrom = lineStart >= 0 ? lineStart + 1 : 0;
            var previousFailedEnd = lastFailed > 0 ? lastFailed - 1 : 0;
            var previousBoundary = Math.Max(
                normalized.LastIndexOf(buildBanner, lastFailed, StringComparison.Ordinal),
                Math.Max(
                    normalized.LastIndexOf("Build succeeded", lastFailed, StringComparison.OrdinalIgnoreCase),
                    lastFailed > 0
                        ? Math.Max(
                            normalized.LastIndexOf("Build FAILED", previousFailedEnd, StringComparison.Ordinal),
                            normalized.LastIndexOf("Build failed with", previousFailedEnd, StringComparison.OrdinalIgnoreCase))
                        : -1));
            if (previousBoundary >= 0 && previousBoundary < lastFailed)
            {
                searchFrom = previousBoundary;
            }

            return normalized[searchFrom..];
        }

        return normalized[start..];
    }

    /// <summary>
    /// Collects all error lines plus up to <paramref name="maxWarnings"/> warning lines.
    /// Errors are never capped so they are not lost behind large warning counts.
    /// </summary>
    public static IReadOnlyList<LogIssue> ParseIssues(string logText, int maxWarnings = 2000)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return [];
        }

        var normalized = logText.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var errors = new List<LogIssue>();
        var warnings = new List<LogIssue>(Math.Min(maxWarnings, lines.Length));

        for (var i = 0; i < lines.Length; i++)
        {
            var line = StripAnsi(lines[i].TrimEnd('\r'));
            if (string.IsNullOrWhiteSpace(line) || IsSummaryLine(line))
            {
                continue;
            }

            if (IsErrorLine(line))
            {
                errors.Add(new LogIssue(i, line, true));
            }
            else if (IsWarningLine(line) && warnings.Count < maxWarnings)
            {
                warnings.Add(new LogIssue(i, line, false));
            }
        }

        return errors
            .Concat(warnings)
            .OrderBy(issue => issue.LineNumber)
            .ToList();
    }

    public static int GetCharacterOffset(string logText, int lineNumber)
    {
        if (lineNumber <= 0)
        {
            return 0;
        }

        var normalized = logText.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var offset = 0;

        for (var i = 0; i < lineNumber && i < lines.Length; i++)
        {
            offset += lines[i].Length + 1;
        }

        return offset;
    }

    private static int ParseBuildSummaryCount(string logText, bool warnings)
    {
        var terminalRegex = warnings ? TerminalWarningCountRegex : TerminalErrorCountRegex;
        var classicRegex = warnings ? ClassicWarningSummaryRegex : ClassicErrorSummaryRegex;

        var fromBuildLine = TryParseCountFromBuildSummaryLine(logText, terminalRegex, classicRegex);
        if (fromBuildLine >= 0)
        {
            return fromBuildLine;
        }

        return ParseLastSummaryCount(logText, classicRegex);
    }

    private static int TryParseCountFromBuildSummaryLine(
        string segment,
        Regex terminalRegex,
        Regex classicRegex)
    {
        var lines = segment.Replace("\r\n", "\n").Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = StripAnsi(lines[i].Trim());
            if (!line.StartsWith("Build ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var terminal = ParseLastMatchCount(line, terminalRegex);
            if (terminal >= 0)
            {
                return terminal;
            }

            var classic = ParseLastMatchCount(line, classicRegex);
            if (classic >= 0)
            {
                return classic;
            }

            return -1;
        }

        return -1;
    }

    private static int ParseLastSummaryCount(string logText, Regex regex)
    {
        var matches = regex.Matches(logText);
        if (matches.Count == 0)
        {
            return -1;
        }

        return int.Parse(matches[^1].Groups[1].Value);
    }

    private static int ParseLastMatchCount(string text, Regex regex)
    {
        var matches = regex.Matches(text);
        if (matches.Count == 0)
        {
            return -1;
        }

        return int.Parse(matches[^1].Groups[1].Value);
    }

    private static bool IsSummaryLine(string line)
    {
        var trimmed = line.TrimEnd('.', ' ');
        if (trimmed.Equals("Build FAILED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ClassicErrorSummaryRegex.IsMatch(line)
            || ClassicWarningSummaryRegex.IsMatch(line)
            || TerminalErrorCountRegex.IsMatch(line)
            || TerminalWarningCountRegex.IsMatch(line);
    }

    private static bool IsErrorLine(string line) =>
        ErrorMarkers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase))
        || CompilerErrorRegex.IsMatch(line);

    private static bool IsWarningLine(string line) =>
        !IsErrorLine(line)
        && (WarningMarkers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase))
            || CompilerWarningRegex.IsMatch(line));

    private static string StripAnsi(string line) =>
        Regex.Replace(line, @"\x1b\[[0-9;]*m", string.Empty);
}
