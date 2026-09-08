using System.Text.RegularExpressions;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Compact one-line formatter for known compiler/MSBuild/NuGet error previews (#111a).
/// Not a general log summarizer — unknown shapes keep a trimmed raw line.
/// </summary>
public static class BuildErrorCompactFormatter
{
    private const int MaxRawLength = 220;

    // path(line,col): error CSxxxx: message  OR  error MSBxxxx: message
    private static readonly Regex StructuredErrorRegex = new(
        @"^(?:(?<file>.+?)\((?<line>\d+)(?:,\d+)?\)\s*:\s*)?error\s+(?<code>(?:CS|MSB|NU)\d+)\s*:\s*(?<msg>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// Returns a compact <c>CODE · file:line · message</c> line when the preview matches a known form;
    /// otherwise a single trimmed raw line (never invents structure).
    /// </summary>
    public static string Format(string? preview)
    {
        if (string.IsNullOrWhiteSpace(preview))
        {
            return string.Empty;
        }

        var line = FirstNonEmptyLine(preview);
        if (line.Length == 0)
        {
            return string.Empty;
        }

        var match = StructuredErrorRegex.Match(line);
        if (!match.Success)
        {
            return TrimOneLine(line);
        }

        var code = match.Groups["code"].Value.ToUpperInvariant();
        var message = match.Groups["msg"].Value.Trim();
        if (message.Length == 0)
        {
            return TrimOneLine(line);
        }

        if (!match.Groups["file"].Success || !match.Groups["line"].Success)
        {
            return $"{code} · {TrimOneLine(message, MaxRawLength - code.Length - 3)}";
        }

        var fileName = Path.GetFileName(match.Groups["file"].Value.Trim().Trim('"'));
        var lineNo = match.Groups["line"].Value;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return $"{code} · {TrimOneLine(message, MaxRawLength - code.Length - 3)}";
        }

        var head = $"{code} · {fileName}:{lineNo}";
        var budget = MaxRawLength - head.Length - 3;
        if (budget < 24)
        {
            return TrimOneLine($"{head} · {message}");
        }

        return $"{head} · {TrimOneLine(message, budget)}";
    }

    private static string FirstNonEmptyLine(string text)
    {
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return string.Empty;
    }

    private static string TrimOneLine(string text, int maxLength = MaxRawLength)
    {
        var trimmed = text.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        if (maxLength <= 1)
        {
            return "…";
        }

        return trimmed[..(maxLength - 1)].TrimEnd() + "…";
    }
}
