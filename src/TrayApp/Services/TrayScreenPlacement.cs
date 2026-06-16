using System.Drawing;
using System.Windows;
using FormsScreen = System.Windows.Forms.Screen;

namespace BuildMonitor.TrayApp.Services;

/// <summary>
/// Places WPF windows on the monitor where the user last interacted with the tray icon.
/// </summary>
public static class TrayScreenPlacement
{
    private static Rectangle lastTrayWorkArea = GetPrimaryWorkArea();

    public static void CaptureFromCursor()
    {
        lastTrayWorkArea = FormsScreen.FromPoint(Cursor.Position).WorkingArea;
    }

    public static Rectangle GetWorkArea() => lastTrayWorkArea;

    public static void PlaceWindowCentered(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;

        var area = GetWorkArea();
        var width = ResolveDimension(window.ActualWidth, window.Width, 800);
        var height = ResolveDimension(window.ActualHeight, window.Height, 600);

        window.Left = area.Left + Math.Max(0, (area.Width - width) / 2.0);
        window.Top = area.Top + Math.Max(0, (area.Height - height) / 2.0);
    }

    public static void PlaceNearTrayBottomRight(Window window, double margin = 12)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.UpdateLayout();

        var area = GetWorkArea();
        var width = ResolveDimension(window.ActualWidth, window.Width, 360);
        var height = ResolveDimension(window.ActualHeight, window.Height, 420);

        window.Left = area.Right - width - margin;
        window.Top = area.Bottom - height - margin;
    }

    private static double ResolveDimension(double actual, double design, double fallback)
    {
        if (actual > 0)
        {
            return actual;
        }

        if (design > 0 && !double.IsNaN(design))
        {
            return design;
        }

        return fallback;
    }

    private static Rectangle GetPrimaryWorkArea() =>
        FormsScreen.PrimaryScreen?.WorkingArea
        ?? new Rectangle(0, 0, 1920, 1080);
}
