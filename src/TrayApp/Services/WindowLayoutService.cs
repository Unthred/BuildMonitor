using System.Windows;
using FormsScreen = System.Windows.Forms.Screen;

namespace BuildMonitor.TrayApp.Services;

public static class WindowLayoutService
{
    public static void Apply(Window window, WindowLayoutState state, double defaultWidth, double defaultHeight)
    {
        if (double.IsFinite(state.Width) && state.Width >= window.MinWidth)
        {
            window.Width = state.Width;
        }
        else if (defaultWidth > 0)
        {
            window.Width = defaultWidth;
        }

        if (double.IsFinite(state.Height) && state.Height >= window.MinHeight)
        {
            window.Height = state.Height;
        }
        else if (defaultHeight > 0)
        {
            window.Height = defaultHeight;
        }

        if (double.IsFinite(state.Left) && double.IsFinite(state.Top)
            && IsOnScreen(state.Left, state.Top, window.Width, window.Height))
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
            AssignIfFinite(bounds.Left, v => state.Left = v);
            AssignIfFinite(bounds.Top, v => state.Top = v);
        }

        AssignIfFinite(bounds.Width, v => state.Width = v);
        AssignIfFinite(bounds.Height, v => state.Height = v);
        state.WindowState = (int)window.WindowState;
    }

    private static void AssignIfFinite(double value, Action<double> assign)
    {
        if (double.IsFinite(value))
        {
            assign(value);
        }
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
