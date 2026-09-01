using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace GenerateTrayIcons;

/// <summary>
/// Extracts tray icon states from the externally supplied production master sheet.
/// No redraw — crop, normalize framing, and high-quality downscale only.
/// </summary>
internal static class ProductionMasterExtractor
{
    private const byte AlphaThreshold = 16;
    private const int PaddingPx = 8;

    private static readonly int[] OutputSizes = [16, 20, 24, 32];

    /// <summary>
    /// Master layout: 1536×1024, three columns × two rows.
    /// Bottom-middle cell is empty; Failed and Neutral occupy bottom-left/right.
    /// </summary>
    private static readonly (string AssetName, Rectangle Cell, string StateLabel)[] SourceCells =
    [
        ("tray-healthy", new Rectangle(0, 0, 512, 512), "Healthy (green check)"),
        ("tray-building", new Rectangle(512, 0, 512, 512), "Building (orange hammer)"),
        ("tray-attention", new Rectangle(1024, 0, 512, 512), "Attention (yellow !)"),
        ("tray-failed", new Rectangle(0, 512, 512, 512), "Failed (red X)"),
        ("tray-neutral", new Rectangle(1024, 512, 512, 512), "Neutral (grey minus)"),
    ];

    internal sealed record ExtractionReport(
        string MasterPath,
        Size MasterSize,
        IReadOnlyList<CellExtraction> Cells,
        int NormalizedSquareSize,
        bool BadgeLessDuckAvailable);

    internal sealed record CellExtraction(
        string AssetName,
        string StateLabel,
        Rectangle SourceCell,
        Rectangle ContentBounds,
        Rectangle NormalizedPlacement);

    public static ExtractionReport Inspect(string masterPath)
    {
        using var master = LoadMaster(masterPath);
        var cells = MeasureCells(master);
        var square = ComputeNormalizedSquare(cells);
        return new ExtractionReport(
            masterPath,
            master.Size,
            cells,
            square,
            BadgeLessDuckAvailable: false);
    }

    public static void GenerateAssets(string masterPath, string runtimeDir, string pngDir)
    {
        Directory.CreateDirectory(runtimeDir);
        Directory.CreateDirectory(pngDir);

        using var master = LoadMaster(masterPath);
        var cells = MeasureCells(master);
        var squareSize = ComputeNormalizedSquare(cells);

        Console.WriteLine($"Master: {masterPath} ({master.Width}x{master.Height})");
        Console.WriteLine($"Normalized square: {squareSize}x{squareSize}px (padding {PaddingPx}px)");
        Console.WriteLine();

        foreach (var cell in cells)
        {
            using var normalized = RenderNormalizedMaster(master, cell, squareSize);
            var bitmaps = new List<Bitmap>();
            foreach (var size in OutputSizes)
            {
                var resized = ResizeHighQuality(normalized, size);
                bitmaps.Add(resized);
                resized.Save(Path.Combine(pngDir, $"{cell.AssetName}-{size}.png"), ImageFormat.Png);
            }

            IconFileWriter.WriteMultiSizeIcon(Path.Combine(runtimeDir, $"{cell.AssetName}.ico"), bitmaps);
            foreach (var bitmap in bitmaps)
            {
                bitmap.Dispose();
            }

            Console.WriteLine(
                $"{cell.AssetName}: cell={FormatRect(cell.SourceCell)} content={FormatRect(cell.ContentBounds)} placement={FormatRect(cell.NormalizedPlacement)}");
        }
    }

    private static Bitmap LoadMaster(string masterPath)
    {
        if (!File.Exists(masterPath))
        {
            throw new FileNotFoundException("Production master PNG not found.", masterPath);
        }

        var bitmap = new Bitmap(masterPath);
        if (bitmap.PixelFormat is not (PixelFormat.Format32bppArgb or PixelFormat.Format32bppPArgb))
        {
            var converted = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(converted);
            g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);
            bitmap.Dispose();
            bitmap = converted;
        }

        return bitmap;
    }

    private static List<CellExtraction> MeasureCells(Bitmap master)
    {
        var squareSize = 0;
        var measured = new List<(string AssetName, string StateLabel, Rectangle SourceCell, Rectangle ContentBounds)>();

        foreach (var (assetName, cell, label) in SourceCells)
        {
            var bounds = FindContentBounds(master, cell);
            if (bounds.IsEmpty)
            {
                throw new InvalidOperationException($"No opaque artwork found in cell {assetName} ({FormatRect(cell)}).");
            }

            measured.Add((assetName, label, cell, bounds));
            squareSize = Math.Max(squareSize, Math.Max(bounds.Width, bounds.Height));
        }

        squareSize += PaddingPx * 2;

        var result = new List<CellExtraction>();
        foreach (var (assetName, label, cell, bounds) in measured)
        {
            var placement = CenterInSquare(bounds, squareSize);
            result.Add(new CellExtraction(assetName, label, cell, bounds, placement));
        }

        return result;
    }

    private static int ComputeNormalizedSquare(IReadOnlyList<CellExtraction> cells)
    {
        var maxExtent = 0;
        foreach (var cell in cells)
        {
            maxExtent = Math.Max(maxExtent, Math.Max(cell.ContentBounds.Width, cell.ContentBounds.Height));
        }

        return maxExtent + PaddingPx * 2;
    }

    private static Rectangle FindContentBounds(Bitmap master, Rectangle cell)
    {
        var minX = cell.Right;
        var minY = cell.Bottom;
        var maxX = cell.Left;
        var maxY = cell.Top;

        for (var y = cell.Top; y < cell.Bottom; y++)
        {
            for (var x = cell.Left; x < cell.Right; x++)
            {
                if (master.GetPixel(x, y).A <= AlphaThreshold)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return minX <= maxX && minY <= maxY
            ? Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1)
            : Rectangle.Empty;
    }

    private static Rectangle CenterInSquare(Rectangle content, int squareSize)
    {
        var offsetX = (squareSize - content.Width) / 2;
        var offsetY = (squareSize - content.Height) / 2;
        return new Rectangle(offsetX, offsetY, content.Width, content.Height);
    }

    private static Bitmap RenderNormalizedMaster(Bitmap master, CellExtraction cell, int squareSize)
    {
        var canvas = new Bitmap(squareSize, squareSize, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var source = cell.ContentBounds;
            var dest = cell.NormalizedPlacement;
            g.DrawImage(
                master,
                dest,
                source,
                GraphicsUnit.Pixel);
        }

        return canvas;
    }

    private static Bitmap ResizeHighQuality(Bitmap source, int size)
    {
        var dest = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dest);
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, size, size));
        return dest;
    }

    private static string FormatRect(Rectangle rect) => $"{rect.X},{rect.Y},{rect.Width}x{rect.Height}";
}
