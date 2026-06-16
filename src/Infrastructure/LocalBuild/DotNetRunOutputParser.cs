using System.Text.RegularExpressions;

namespace BuildMonitor.Infrastructure.LocalBuild;

public static partial class DotNetRunOutputParser
{
    private static readonly string[] RunErrorMarkers =
    [
        "Unhandled exception",
        "Application startup exception",
        "fail:",
        "crit:",
        "error:",
        "Exception:",
        "host terminated unexpectedly",
        "Failed to bind",
        "address already in use",
        "Stack trace:",
    ];

    private static readonly string[] RunWarningMarkers =
    [
        "warn:",
        "warning:",
    ];

    public static bool TryExtractListeningUrl(string line, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var match = ListeningUrlRegex().Match(line);
        if (!match.Success)
        {
            return false;
        }

        url = match.Groups["url"].Value.Trim();
        return url.Length > 0;
    }

    public static bool IsHostTerminatedLine(string line) =>
        !string.IsNullOrWhiteSpace(line)
        && line.Contains("host terminated unexpectedly", StringComparison.OrdinalIgnoreCase);

    public static bool IsFatalStartupLine(string line) =>
        !string.IsNullOrWhiteSpace(line)
        && (line.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Application startup exception", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Failed to bind", StringComparison.OrdinalIgnoreCase)
            || line.Contains("address already in use", StringComparison.OrdinalIgnoreCase));

    public static int ParseErrorCount(string logText) =>
        ParseIssues(logText).Count(i => i.IsError);

    public static int ParseWarningCount(string logText) =>
        ParseIssues(logText).Count(i => !i.IsError);

    public static IReadOnlyList<LogIssue> ParseIssues(string logText, int maxIssues = 2000)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return [];
        }

        var normalized = logText.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var issues = new List<LogIssue>(Math.Min(maxIssues, lines.Length));

        for (var i = 0; i < lines.Length && issues.Count < maxIssues; i++)
        {
            var line = StripAnsi(lines[i].TrimEnd('\r'));
            if (string.IsNullOrWhiteSpace(line) || IsNoiseLine(line))
            {
                continue;
            }

            if (IsRunErrorLine(line))
            {
                issues.Add(new LogIssue(i, line, true));
            }
            else if (IsRunWarningLine(line))
            {
                issues.Add(new LogIssue(i, line, false));
            }
        }

        return issues;
    }

    private static bool IsRunErrorLine(string line)
    {
        if (IsFatalStartupLine(line) || IsHostTerminatedLine(line))
        {
            return true;
        }

        if (RunErrorRegex().IsMatch(line))
        {
            return true;
        }

        return RunErrorMarkers.Any(marker =>
            line.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRunWarningLine(string line) =>
        RunWarningMarkers.Any(marker =>
            line.Contains(marker, StringComparison.OrdinalIgnoreCase))
        && !IsRunErrorLine(line);

    private static bool IsNoiseLine(string line) =>
        line.StartsWith("dotnet watch", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("🔥", StringComparison.Ordinal)
        || line.StartsWith("⌚", StringComparison.Ordinal)
        || line.Contains("Hot reload enabled", StringComparison.OrdinalIgnoreCase);

    private static string StripAnsi(string line) =>
        AnsiRegex().Replace(line, string.Empty);

    [GeneratedRegex(
        @"(?:Now listening on:|Listening on)\s*(?<url>https?://\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ListeningUrlRegex();

    [GeneratedRegex(
        @"\b(fail|crit|error)\s*:",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RunErrorRegex();

    [GeneratedRegex(@"\x1B\[[0-9;]*m", RegexOptions.Compiled)]
    private static partial Regex AnsiRegex();
}
