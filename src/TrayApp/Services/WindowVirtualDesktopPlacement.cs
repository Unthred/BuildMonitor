using System.Windows;
using System.Windows.Interop;

namespace BuildMonitor.TrayApp.Services;

internal static class WindowVirtualDesktopPlacement
{
    public static void TryFollow(Window window, bool enabled)
    {
        if (!enabled)
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        VirtualDesktopInterop.TryFollowCurrentVirtualDesktop(handle);
    }
}
