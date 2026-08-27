namespace BuildMonitor.Core.Rules;

/// <summary>
/// Single-window-per-project decision used by the tray log viewer opener.
/// </summary>
public static class LogViewerWindowReuse
{
    /// <summary>
    /// When true, activate the existing viewer instead of constructing a new window.
    /// </summary>
    public static bool ShouldActivateExisting(bool hasOpenEntry, bool windowIsLoaded) =>
        hasOpenEntry && windowIsLoaded;
}
