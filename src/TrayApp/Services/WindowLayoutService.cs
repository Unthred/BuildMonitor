using System.Windows;
using FormsScreen = System.Windows.Forms.Screen;

namespace BuildMonitor.TrayApp.Services;

public static class WindowLayoutService
{
    public static void Apply(Window window, WindowLayoutState state, double defaultWidth, double defaultHeight)
    {
        if (state.Width >= window.MinWidth && !double.IsNaN(state.Width))
        {
            window.Width = state.Width;
        }
        else if (defaultWidth > 0)
        {
            window.Width = defaultWidth;
        }

        if (state.Height >= window.MinHeight && !double.IsNaN(state.Height))
        {
            window.Height = state.Height;
        }
        else if (defaultHeight > 0)
        {
            window.Height = defaultHeight;
        }

        if (!double.IsNaN(state.Left) && !double.IsNaN(state.Top) && IsOnScreen(state.Left, state.Top, window.Width, window.Height))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = state.Left;
            window.Top = state.Top;
        }

        if (state.WindowState is (int)WindowState.Maximized or (int)WindowState.Minimized)
        {
            window.WindowState = (WindowState)state.WindowState;
        }
    }

    public static void Capture(Window window, WindowLayoutState state, bool sizeOnly = false)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;

        if (!sizeOnly)
        {
            state.Left = bounds.Left;
            state.Top = bounds.Top;
        }

        state.Width = bounds.Width;
        state.Height = bounds.Height;
        state.WindowState = (int)window.WindowState;
    }

    private static bool IsOnScreen(double left, double top, double width, double height)
    {
        var rect = new System.Drawing.Rectangle(
            (int)Math.Round(left),
            (int)Math.Round(top),
            (int)Math.Max(1, Math.Round(width)),
            (int)Math.Max(1, Math.Round(height)));

        return FormsScreen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(rect));
    }
}
