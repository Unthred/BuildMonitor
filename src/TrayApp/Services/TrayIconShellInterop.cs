using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BuildMonitor.TrayApp.Services;

/// <summary>
/// Resolves tray icon screen bounds for the custom hover hint (hide when cursor leaves the icon).
/// Native shell tooltip is suppressed via <see cref="NotifyIcon.Text"/> = empty — do not call
/// <c>Shell_NotifyIcon(NIM_MODIFY)</c> with <c>NIF_MESSAGE</c>; that clears the callback and breaks the menu.
/// </summary>
internal static class TrayIconShellInterop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out RectNative rect);

    public static bool TryGetIconScreenBounds(NotifyIcon notifyIcon, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var windowHandle = GetNotifyIconWindowHandle(notifyIcon);
        var iconId = GetNotifyIconId(notifyIcon);
        if (windowHandle == IntPtr.Zero || iconId == 0)
        {
            return false;
        }

        var identifier = new NotifyIconIdentifier
        {
            cbSize = Marshal.SizeOf<NotifyIconIdentifier>(),
            hWnd = windowHandle,
            uID = (int)iconId
        };

        if (Shell_NotifyIconGetRect(ref identifier, out var rect) != 0)
        {
            return false;
        }

        bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    public static bool TryGetNotifyIconWindowHandle(NotifyIcon notifyIcon, out IntPtr windowHandle)
    {
        windowHandle = GetNotifyIconWindowHandle(notifyIcon);
        return windowHandle != IntPtr.Zero;
    }

    public static bool IsCursorOverIcon(NotifyIcon notifyIcon, int inflatePixels = 12)
    {
        if (!TryGetIconScreenBounds(notifyIcon, out var bounds))
        {
            // Cannot resolve icon bounds — do not treat as "left icon".
            return true;
        }

        if (inflatePixels > 0)
        {
            bounds.Inflate(inflatePixels, inflatePixels);
        }

        return bounds.Contains(Control.MousePosition);
    }

    private static IntPtr GetNotifyIconWindowHandle(NotifyIcon notifyIcon)
    {
        if (GetInstanceField(notifyIcon, "_window", "window") is NativeWindow window)
        {
            return window.Handle;
        }

        return IntPtr.Zero;
    }

    private static uint GetNotifyIconId(NotifyIcon notifyIcon)
    {
        var value = GetInstanceField(notifyIcon, "_id", "id");
        return value switch
        {
            uint uintId => uintId,
            int intId => (uint)intId,
            _ => 0
        };
    }

    private static object? GetInstanceField(NotifyIcon notifyIcon, params string[] names)
    {
        foreach (var name in names)
        {
            var field = typeof(NotifyIcon).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field is not null)
            {
                return field.GetValue(notifyIcon);
            }
        }

        return null;
    }
}
