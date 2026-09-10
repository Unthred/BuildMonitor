namespace GenerateTrayIcons;

internal static class Program
{
    public static int Main(string[] args)
    {
        var repoRoot = ResolveRepoRoot(args);
        var duckBasePath = Path.Combine(repoRoot, "docs", "assets", "tray-duck-base-badge-less.png");
        var runtimeDir = Path.Combine(repoRoot, "src", "TrayApp", "Assets", "tray", "runtime");
        var pngDir = Path.Combine(repoRoot, "src", "TrayApp", "Assets", "tray", "png");
        var sheetsDir = Path.Combine(repoRoot, "docs", "assets", "tray-ring-129");
        var legacyMasterPath = Path.Combine(repoRoot, "docs", "assets", "tray-icon-production-masters.png");

        if (args.Any(a => string.Equals(a, "--inspect", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"Badge-less duck base: {duckBasePath}");
            Console.WriteLine($"Exists: {File.Exists(duckBasePath)}");
            if (File.Exists(legacyMasterPath))
            {
                var report = ProductionMasterExtractor.Inspect(legacyMasterPath);
                Console.WriteLine($"Legacy glyph master: {report.MasterPath} ({report.MasterSize.Width}x{report.MasterSize.Height})");
                Console.WriteLine($"Badge-less duck in legacy master: {report.BadgeLessDuckAvailable}");
            }

            Console.WriteLine($"Ring frames: {HealthRingAssetComposer.FrameCount}, arc: {HealthRingAssetComposer.ArcDegrees}°");
            return 0;
        }

        if (args.Any(a => string.Equals(a, "--legacy-glyph-master", StringComparison.OrdinalIgnoreCase)))
        {
            ProductionMasterExtractor.GenerateAssets(legacyMasterPath, runtimeDir, pngDir);
            Console.WriteLine("Regenerated legacy glyph assets (not used by #129 runtime).");
            return 0;
        }

        HealthRingAssetComposer.Generate(duckBasePath, runtimeDir, pngDir, sheetsDir);
        Console.WriteLine($"PNG previews: {pngDir}");
        Console.WriteLine($"ICO runtime assets: {runtimeDir}");
        Console.WriteLine($"Acceptance sheets: {sheetsDir}");
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
