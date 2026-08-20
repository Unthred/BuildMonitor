using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BuildMonitor.TrayApp.Services;

/// <summary>
/// Re-anchors tray and persisted windows when the display topology changes (RDP, unplug monitor).
/// </summary>
internal sealed class WindowDisplayChangeWatcher : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly Func<IEnumerable<Window>> windows;
    private readonly Action? invalidateHoverPlacementCache;
    private readonly Action? refreshHoverTrayPlacement;
    private readonly Func<bool> isExiting;
    private bool disposed;

    public WindowDisplayChangeWatcher(
        Dispatcher dispatcher,
        Func<bool> isExiting,
        Func<IEnumerable<Window>> windows,
        Action? invalidateHoverPlacementCache,
        Action? refreshHoverTrayPlacement)
    {
        this.dispatcher = dispatcher;
        this.isExiting = isExiting;
        this.windows = windows;
        this.invalidateHoverPlacementCache = invalidateHoverPlacementCache;
        this.refreshHoverTrayPlacement = refreshHoverTrayPlacement;
        SystemEvents.DisplaySettingsChanged += OnSystemDisplaySettingsChanged;
    }

    public static void RecoverVisibleWindows(
        Action? invalidateHoverPlacementCache,
        Action? refreshHoverTrayPlacement,
        IEnumerable<Window> windows)
    {
        try
        {
            TrayScreenPlacement.CaptureFromCursor();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Tray work-area capture failed after display change: {ex.Message}");
        }

        invalidateHoverPlacementCache?.Invoke();
        refreshHoverTrayPlacement?.Invoke();

        foreach (var window in windows)
        {
            try
            {
                if (window.IsLoaded)
                {
                    WindowLayoutService.EnsureVisible(window);
                }
            }
            catch (InvalidOperationException)
            {
                // Window may have been closed between enumeration and ensure.
            }
        }
    }

    private void OnSystemDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (disposed)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, Recover);
    }

    private void Recover()
    {
        if (disposed || isExiting())
        {
            return;
        }

        RecoverVisibleWindows(invalidateHoverPlacementCache, refreshHoverTrayPlacement, windows());
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnSystemDisplaySettingsChanged;
    }
}
