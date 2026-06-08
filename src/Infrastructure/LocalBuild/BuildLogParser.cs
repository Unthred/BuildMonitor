using System.Text.RegularExpressions;

namespace BuildMonitor.Infrastructure.LocalBuild;

public sealed record LogIssue(int LineNumber, string Text, bool IsError);

public static class BuildLogParser
{
    private static readonly Regex WarningSummaryRegex = new(
        @"(\d+)\s+Warning\(s\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ErrorSummaryRegex = new(
        @"(\d+)\s+Error\(s\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CompilerErrorRegex = new(
        @"\berror\s+(CS|MSB|NU|BC|SA|IDE|CA|FS|VB|AD|SYSLIB|NETSDK|CS)\d+\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    public static int ParseErrorCount(string logText)
    {
        var summary = ParseLastSummaryCount(logText, ErrorSummaryRegex);
        if (summary >= 0)
        {
            return summary;
        }

        return ParseIssues(logText).Count(i => i.IsError);
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

    public static int ParseWarningCount(string logText)
    {
        var summary = ParseLastSummaryCount(logText, WarningSummaryRegex);
        if (summary >= 0)
        {
            return summary;
        }

        return ParseIssues(logText).Count(i => !i.IsError);
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

    private static int ParseLastSummaryCount(string logText, Regex regex)
    {
        var matches = regex.Matches(logText);
        if (matches.Count == 0)
        {
            return -1;
        }

        return int.Parse(matches[^1].Groups[1].Value);
    }

    private static bool IsSummaryLine(string line) =>
        ErrorSummaryRegex.IsMatch(line) || WarningSummaryRegex.IsMatch(line);

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
