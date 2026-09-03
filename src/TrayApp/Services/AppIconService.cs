using System.Drawing;
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
#pragma warning disable CS0618
        appIcon ??= LoadIconFromResource() ?? TrafficLightIconFactory.GetShowcaseIcon();
#pragma warning restore CS0618

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

    public static System.Drawing.Icon CreateTrafficLightAppIcon() =>
#pragma warning disable CS0618
        TrafficLightIconFactory.GetShowcaseIcon();
#pragma warning restore CS0618
}
