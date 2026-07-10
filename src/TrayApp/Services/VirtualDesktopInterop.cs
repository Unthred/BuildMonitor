using System.Runtime.InteropServices;

namespace BuildMonitor.TrayApp.Services;

/// <summary>
/// Moves top-level windows onto the same Windows virtual desktop as a reference HWND (e.g. tray icon).
/// </summary>
internal static class VirtualDesktopInterop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
    }

    [ComImport]
    [Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
    private class VirtualDesktopManager;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out int onCurrentDesktop);

        void GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);

        void MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out PointNative point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(PointNative point);

    public static bool IsOnCurrentVirtualDesktop(IntPtr window)
    {
        if (!OperatingSystem.IsWindows() || window == IntPtr.Zero)
        {
            return true;
        }

        try
        {
            var manager = (IVirtualDesktopManager)new VirtualDesktopManager();
            return manager.IsWindowOnCurrentVirtualDesktop(window, out var onCurrent) == 0
                && onCurrent != 0;
        }
        catch
        {
            return true;
        }
    }

    public static bool TryMoveToSameDesktop(IntPtr window, IntPtr referenceWindow)
    {
        if (!OperatingSystem.IsWindows()
            || window == IntPtr.Zero
            || referenceWindow == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var manager = (IVirtualDesktopManager)new VirtualDesktopManager();
            manager.GetWindowDesktopId(referenceWindow, out var desktopId);
            manager.MoveWindowToDesktop(window, ref desktopId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Moves a window onto the desktop the user is viewing (foreground / cursor).
    /// </summary>
    public static bool TryFollowCurrentVirtualDesktop(IntPtr window)
    {
        if (!OperatingSystem.IsWindows() || window == IntPtr.Zero)
        {
            return false;
        }

        var reference = GetForegroundWindow();
        if (reference == IntPtr.Zero || reference == window)
        {
            GetCursorPos(out var point);
            reference = WindowFromPoint(point);
        }

        if (reference == IntPtr.Zero || reference == window)
        {
            return false;
        }

        return TryMoveToSameDesktop(window, reference);
    }
}
