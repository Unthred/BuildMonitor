using System.Text.RegularExpressions;

namespace BuildMonitor.Infrastructure.LocalBuild;

public static partial class DotNetRunOutputParser
{
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

    [GeneratedRegex(
        @"(?:Now listening on:|Listening on)\s*(?<url>https?://\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ListeningUrlRegex();
}
