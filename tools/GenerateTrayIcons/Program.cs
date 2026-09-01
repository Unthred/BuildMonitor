using System.Drawing;

namespace GenerateTrayIcons;

internal static class Program
{
    private static readonly int[] TraySizes = [16, 20, 24, 32];
    private static readonly int[] AppSizes = [16, 20, 24, 32, 48, 256];

    public static int Main(string[] args)
    {
        var repoRoot = ResolveRepoRoot(args);
        var runtimeDir = Path.Combine(repoRoot, "src", "TrayApp", "Assets", "tray", "runtime");
        var pngDir = Path.Combine(repoRoot, "src", "TrayApp", "Assets", "tray", "png");
        var appIconPath = Path.Combine(repoRoot, "src", "TrayApp", "Assets", "AppIcon.ico");

        Directory.CreateDirectory(runtimeDir);
        Directory.CreateDirectory(pngDir);

        WriteTrayState(runtimeDir, pngDir, "tray-neutral", DuckBadgeKind.Neutral);
        WriteTrayState(runtimeDir, pngDir, "tray-healthy", DuckBadgeKind.Healthy);
        WriteTrayState(runtimeDir, pngDir, "tray-building", DuckBadgeKind.Building);
        WriteTrayState(runtimeDir, pngDir, "tray-attention", DuckBadgeKind.Attention);
        WriteTrayState(runtimeDir, pngDir, "tray-failed", DuckBadgeKind.Failed);

        WriteAppIcon(appIconPath, pngDir);

        Console.WriteLine($"Generated tray icons in {runtimeDir}");
        Console.WriteLine($"Generated PNG previews in {pngDir}");
        Console.WriteLine($"Generated application icon {appIconPath}");
        return 0;
    }

    private static void WriteTrayState(string runtimeDir, string pngDir, string name, DuckBadgeKind badge)
    {
        var bitmaps = new List<Bitmap>();
        foreach (var size in TraySizes)
        {
            var bitmap = BuilderDuckRenderer.Render(size, badge);
            bitmaps.Add(bitmap);
            bitmap.Save(Path.Combine(pngDir, $"{name}-{size}.png"), System.Drawing.Imaging.ImageFormat.Png);
        }

        IconFileWriter.WriteMultiSizeIcon(Path.Combine(runtimeDir, $"{name}.ico"), bitmaps);
        foreach (var bitmap in bitmaps)
        {
            bitmap.Dispose();
        }
    }

    private static void WriteAppIcon(string path, string pngDir)
    {
        var bitmaps = new List<Bitmap>();
        foreach (var size in AppSizes)
        {
            var bitmap = BuilderDuckRenderer.Render(size, DuckBadgeKind.None);
            bitmaps.Add(bitmap);
            bitmap.Save(Path.Combine(pngDir, $"app-icon-{size}.png"), System.Drawing.Imaging.ImageFormat.Png);
        }

        IconFileWriter.WriteMultiSizeIcon(path, bitmaps);
        foreach (var bitmap in bitmaps)
        {
            bitmap.Dispose();
        }
    }

    private static string ResolveRepoRoot(string[] args)
    {
        if (args.Length > 0 && Directory.Exists(args[0]))
        {
            return Path.GetFullPath(args[0]);
        }

        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "BuildMonitor.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Could not locate BuildMonitor repo root.");
    }
}
