using System.Windows;
using BuildMonitor.Core.Rules;
using FormsScreen = System.Windows.Forms.Screen;
using ScreenRect = BuildMonitor.Core.Rules.WindowScreenVisibility.Rect;

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

        if (double.IsFinite(state.Left) && double.IsFinite(state.Top))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            ApplyVisiblePosition(window, state.Left, state.Top);
        }

        if (state.WindowState is (int)WindowState.Maximized or (int)WindowState.Minimized)
        {
            window.WindowState = (WindowState)state.WindowState;
        }
    }

    /// <summary>
    /// Repositions an already-created window when it is no longer sufficiently on a work area
    /// (e.g. after RDP or a monitor was removed).
    /// </summary>
    public static void EnsureVisible(Window window)
    {
        if (window.WindowState == WindowState.Maximized)
        {
            return;
        }

        if (!double.IsFinite(window.Left) || !double.IsFinite(window.Top)
            || window.Width <= 0 || window.Height <= 0)
        {
            return;
        }

        ApplyVisiblePosition(window, window.Left, window.Top);
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

    private static void ApplyVisiblePosition(Window window, double left, double top)
    {
        var workAreas = GetWorkAreas();
        var preferred = ToScreenRect(TrayScreenPlacement.GetWorkArea());
        var candidate = new ScreenRect(left, top, window.Width, window.Height);
        var visible = WindowScreenVisibility.EnsureVisible(candidate, workAreas, preferred);

        window.Left = visible.X;
        window.Top = visible.Y;
        if (Math.Abs(window.Width - visible.Width) > 0.5)
        {
            window.Width = visible.Width;
        }

        if (Math.Abs(window.Height - visible.Height) > 0.5)
        {
            window.Height = visible.Height;
        }
    }

    private static IReadOnlyList<ScreenRect> GetWorkAreas()
    {
        var screens = FormsScreen.AllScreens;
        if (screens.Length == 0)
        {
            return [new ScreenRect(0, 0, 1920, 1080)];
        }

        return screens.Select(s => ToScreenRect(s.WorkingArea)).ToArray();
    }

    private static ScreenRect ToScreenRect(System.Drawing.Rectangle area) =>
        new(area.X, area.Y, area.Width, area.Height);

    private static void AssignIfFinite(double value, Action<double> assign)
    {
        if (double.IsFinite(value))
        {
            assign(value);
        }
    }
}
