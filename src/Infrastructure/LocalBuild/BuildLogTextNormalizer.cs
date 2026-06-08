using System.Text;

namespace BuildMonitor.Infrastructure.LocalBuild;

/// <summary>
/// Fixes console text where UTF-8 bytes were mis-decoded (common when child processes
/// emit Unicode symbols such as dotnet watch's warning glyph).
/// </summary>
public static class BuildLogTextNormalizer
{
    private static readonly Encoding Latin1 = Encoding.GetEncoding(
        "ISO-8859-1",
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);

    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return TryRepairUtf8Mojibake(text);
    }

    private static string TryRepairUtf8Mojibake(string text)
    {
        if (!LooksLikeMojibake(text))
        {
            return text;
        }

        try
        {
            var bytes = Latin1.GetBytes(text);
            var repaired = Encoding.UTF8.GetString(bytes);
            if (repaired.Contains('\uFFFD') || !LooksHealthier(repaired, text))
            {
                return text;
            }

            return repaired;
        }
        catch (EncoderFallbackException)
        {
            return text;
        }
        catch (DecoderFallbackException)
        {
            return text;
        }
    }

    private static bool LooksLikeMojibake(string text) =>
        text.Contains("â", StringComparison.Ordinal)
        || text.Contains("Ã", StringComparison.Ordinal)
        || text.Contains("ï¿½", StringComparison.Ordinal);

    private static bool LooksHealthier(string repaired, string original)
    {
        var repairedHigh = CountHighUnicode(repaired);
        var originalHigh = CountHighUnicode(original);
        if (repairedHigh > originalHigh)
        {
            return true;
        }

        var repairedMojibake = CountMojibakeMarkers(repaired);
        var originalMojibake = CountMojibakeMarkers(original);
        return repairedMojibake < originalMojibake;
    }

    private static int CountHighUnicode(string text)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (ch > 0xFF)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountMojibakeMarkers(string text)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (ch is 'â' or 'Ã' or '\uFFFD')
            {
                count++;
            }
        }

        return count;
    }
}
