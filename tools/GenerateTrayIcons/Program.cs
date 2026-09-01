namespace GenerateTrayIcons;

internal static class Program
{
    public static int Main(string[] args)
    {
        var repoRoot = ResolveRepoRoot(args);
        var masterPath = Path.Combine(repoRoot, "docs", "assets", "tray-icon-production-masters.png");
        var runtimeDir = Path.Combine(repoRoot, "src", "TrayApp", "Assets", "tray", "runtime");
        var pngDir = Path.Combine(repoRoot, "src", "TrayApp", "Assets", "tray", "png");

        if (args.Any(a => string.Equals(a, "--inspect", StringComparison.OrdinalIgnoreCase)))
        {
            var report = ProductionMasterExtractor.Inspect(masterPath);
            Console.WriteLine($"Master: {report.MasterPath}");
            Console.WriteLine($"Size: {report.MasterSize.Width}x{report.MasterSize.Height}");
            Console.WriteLine($"Normalized square: {report.NormalizedSquareSize}px");
            Console.WriteLine($"Badge-less duck in master: {report.BadgeLessDuckAvailable}");
            foreach (var cell in report.Cells)
            {
                Console.WriteLine(
                    $"{cell.AssetName} [{cell.StateLabel}] cell={cell.SourceCell} content={cell.ContentBounds} placement={cell.NormalizedPlacement}");
            }

            return 0;
        }

        ProductionMasterExtractor.GenerateAssets(masterPath, runtimeDir, pngDir);

        Console.WriteLine();
        Console.WriteLine($"PNG previews: {pngDir}");
        Console.WriteLine($"ICO runtime assets: {runtimeDir}");
        Console.WriteLine("Application icon unchanged — master sheet has no badge-less duck.");
        return 0;
    }

    private static string ResolveRepoRoot(string[] args)
    {
        var positional = args.FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(positional) && Directory.Exists(positional))
        {
            return Path.GetFullPath(positional);
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
