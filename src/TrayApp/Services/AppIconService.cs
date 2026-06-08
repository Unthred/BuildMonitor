using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BuildMonitor.TrayApp.Services;

public static class AppIconService
{
    private static System.Drawing.Icon? appIcon;
    private static ImageSource? windowIconSource;

    public static System.Drawing.Icon TrayIcon =>
        appIcon ??= LoadIconFromResource() ?? CreateTrafficLightAppIcon();

    public static ImageSource WindowIcon =>
        windowIconSource ??= Imaging.CreateBitmapSourceFromHIcon(
            TrayIcon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());

    public static void ApplyToWindow(Window window) => window.Icon = WindowIcon;

    private static System.Drawing.Icon? LoadIconFromResource()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("AppIcon.ico", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        return stream is null ? null : new System.Drawing.Icon(stream);
    }

    public static System.Drawing.Icon CreateTrafficLightAppIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(System.Drawing.Color.FromArgb(40, 40, 40));

        using var housing = new SolidBrush(System.Drawing.Color.FromArgb(55, 55, 58));
        graphics.FillRoundedRectangle(housing, new Rectangle(8, 2, 16, 28), 4);

        DrawLamp(graphics, System.Drawing.Color.FromArgb(220, 53, 69), 8);
        DrawLamp(graphics, System.Drawing.Color.FromArgb(255, 193, 7), 16);
        DrawLamp(graphics, System.Drawing.Color.FromArgb(40, 167, 69), 24);

        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    private static void DrawLamp(Graphics graphics, System.Drawing.Color color, int y)
    {
        using var brush = new SolidBrush(color);
        graphics.FillEllipse(brush, 11, y, 10, 10);
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, System.Drawing.Brush brush, Rectangle bounds, int radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
