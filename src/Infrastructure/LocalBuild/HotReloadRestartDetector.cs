using System.Text.RegularExpressions;

namespace BuildMonitor.Infrastructure.LocalBuild;

public enum HotReloadRestartRequest
{
    None = 0,
    RestartApp = 1,
    RebuildAndRestart = 2
}

/// <summary>
/// Detects hot-reload / ENC messages in build or run output that need a restart or rebuild.
/// </summary>
public static partial class HotReloadRestartDetector
{
    private static readonly string[] RebuildPhrases =
    [
        "requires a rebuild",
        "require a rebuild",
        "requires rebuilding",
        "rebuild is required",
        "must be rebuilt",
        "requires recompiling",
    ];

    private static readonly string[] RestartPhrases =
    [
        "requires restarting the application",
        "requires restarting",
        "unable to apply hot reload",
        "rude edit",
        "change failed to apply",
        "hot reload of changes failed",
        "further changes won't be applied",
        "hotreloadexception",
        "restart is needed to apply",
        "restart the application to apply",
    ];

    private static readonly string[] WatchAutoRestartPhrases =
    [
        "unable to apply hot reload because of a rude edit",
        "do you want to restart your app",
    ];

    private static readonly string[] IgnorePhrases =
    [
        "hot reload enabled",
        "hot reload session started",
        "hot reload of static files succeeded",
        "press \"ctrl + r\" to restart",
        "press 'ctrl + r' to restart",
    ];

    public static HotReloadRestartRequest Classify(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return HotReloadRestartRequest.None;
        }

        var normalized = StripAnsi(line.Trim());
        if (normalized.Length == 0 || IsIgnoredLine(normalized))
        {
            return HotReloadRestartRequest.None;
        }

        if (IsInformationalRestartLine(normalized))
        {
            return HotReloadRestartRequest.None;
        }

        if (ContainsAny(normalized, RebuildPhrases))
        {
            return HotReloadRestartRequest.RebuildAndRestart;
        }

        if (ContainsAny(normalized, RestartPhrases))
        {
            return HotReloadRestartRequest.RestartApp;
        }

        return HotReloadRestartRequest.None;
    }

    public static bool IsWatchAutoRestartMessage(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var normalized = StripAnsi(line.Trim());
        return ContainsAny(normalized, WatchAutoRestartPhrases);
    }

    private static bool IsIgnoredLine(string line) =>
        IgnorePhrases.Any(phrase => line.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    private static bool IsInformationalRestartLine(string line)
    {
        if (line.StartsWith("dotnet watch", StringComparison.OrdinalIgnoreCase)
            && line.Contains("restarting", StringComparison.OrdinalIgnoreCase)
            && !line.Contains("requires", StringComparison.OrdinalIgnoreCase)
            && !line.Contains("unable", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return line.Contains("restarted successfully", StringComparison.OrdinalIgnoreCase)
            || line.Contains("application started", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string line, IEnumerable<string> phrases) =>
        phrases.Any(phrase => line.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    private static string StripAnsi(string line) =>
        AnsiRegex().Replace(line, string.Empty);

    [GeneratedRegex(@"\x1B\[[0-9;]*m", RegexOptions.Compiled)]
    private static partial Regex AnsiRegex();
}
