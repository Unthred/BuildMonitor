using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace GenerateTrayIcons;

/// <summary>
/// Composes #129 tray health-ring ICOs from the recovered badge-less duck base.
/// Pre-rendered assets only — no runtime drawing.
/// </summary>
internal static class HealthRingAssetComposer
{
    internal const int FrameCount = 12;
    internal const float ArcDegrees = 120f;

    private static readonly int[] Sizes = [16, 20, 24, 32];

    private static readonly Color Green = Color.FromArgb(255, 40, 180, 70);
    private static readonly Color GreenBright = Color.FromArgb(255, 70, 220, 100);
    private static readonly Color Amber = Color.FromArgb(255, 230, 170, 20);
    private static readonly Color AmberBright = Color.FromArgb(255, 255, 200, 40);
    private static readonly Color Red = Color.FromArgb(255, 220, 60, 60);
    private static readonly Color Grey = Color.FromArgb(255, 140, 140, 145);
    private static readonly Color GreyBright = Color.FromArgb(255, 190, 190, 195);

    private sealed record RingPalette(Color Solid, Color Track, Color Arc);

    private sealed record SizeGeometry(int Size, float Stroke, float DuckInsetPx);

    /// <summary>Size-specific stroke + duck inset (not a universal % scale).</summary>
    private static readonly SizeGeometry[] Geometries =
    [
        new(16, Stroke: 2.0f, DuckInsetPx: 2.0f),
        new(20, Stroke: 2.25f, DuckInsetPx: 2.25f),
        new(24, Stroke: 2.5f, DuckInsetPx: 2.5f),
        new(32, Stroke: 3.0f, DuckInsetPx: 3.0f)
    ];

    public static void Generate(string duckBasePath, string runtimeDir, string pngDir, string sheetsDir)
    {
        Directory.CreateDirectory(runtimeDir);
        Directory.CreateDirectory(pngDir);
        Directory.CreateDirectory(sheetsDir);

        if (!File.Exists(duckBasePath))
        {
            throw new FileNotFoundException("Badge-less duck base not found.", duckBasePath);
        }

        using var duckBase = new Bitmap(duckBasePath);

        foreach (var health in new[] { "healthy", "attention", "failed", "neutral" })
        {
            var palette = PaletteFor(health);
            var staticFrames = new List<Bitmap>();
            foreach (var geo in Geometries)
            {
                var bmp = Compose(duckBase, geo, palette, active: false, frame: 0);
                bmp.Save(Path.Combine(pngDir, $"tray-{health}-{geo.Size}.png"), ImageFormat.Png);
                staticFrames.Add(bmp);
            }

            IconFileWriter.WriteMultiSizeIcon(Path.Combine(runtimeDir, $"tray-{health}.ico"), staticFrames);
            foreach (var b in staticFrames)
            {
                b.Dispose();
            }

            if (health == "failed")
            {
                continue;
            }

            for (var frame = 0; frame < FrameCount; frame++)
            {
                var animFrames = new List<Bitmap>();
                foreach (var geo in Geometries)
                {
                    var bmp = Compose(duckBase, geo, palette, active: true, frame);
                    bmp.Save(Path.Combine(pngDir, $"tray-{health}-a{frame:00}-{geo.Size}.png"), ImageFormat.Png);
                    animFrames.Add(bmp);
                }

                IconFileWriter.WriteMultiSizeIcon(
                    Path.Combine(runtimeDir, $"tray-{health}-a{frame:00}.ico"),
                    animFrames);
                foreach (var b in animFrames)
                {
                    b.Dispose();
                }
            }
        }

        WriteAcceptanceSheets(duckBase, sheetsDir);
        Console.WriteLine($"Health-ring assets written ({4 + 3 * FrameCount} ICOs).");
    }

    private static RingPalette PaletteFor(string health) =>
        health switch
        {
            "healthy" => new RingPalette(Green, Color.FromArgb(90, Green), GreenBright),
            "attention" => new RingPalette(Amber, Color.FromArgb(90, Amber), AmberBright),
            "failed" => new RingPalette(Red, Color.FromArgb(90, Red), Red),
            _ => new RingPalette(Grey, Color.FromArgb(90, Grey), GreyBright)
        };

    private static Bitmap Compose(
        Bitmap duckBase,
        SizeGeometry geo,
        RingPalette palette,
        bool active,
        int frame)
    {
        var bmp = new Bitmap(geo.Size, geo.Size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        var inset = geo.DuckInsetPx;
        var duckSize = geo.Size - inset * 2f;
        g.DrawImage(duckBase, inset, inset, duckSize, duckSize);

        var half = geo.Stroke / 2f;
        var ring = new RectangleF(half, half, geo.Size - geo.Stroke - 0.5f, geo.Size - geo.Stroke - 0.5f);

        if (active)
        {
            using var trackPen = new Pen(palette.Track, geo.Stroke)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawEllipse(trackPen, ring);

            var start = -90f + 360f * frame / FrameCount;
            using var arcPen = new Pen(palette.Arc, geo.Stroke)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawArc(arcPen, ring, start, ArcDegrees);
        }
        else
        {
            using var solid = new Pen(palette.Solid, geo.Stroke)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawEllipse(solid, ring);
        }

        return bmp;
    }

    private static void WriteAcceptanceSheets(Bitmap duckBase, string sheetsDir)
    {
        WriteSheet(duckBase, sheetsDir, dark: true);
        WriteSheet(duckBase, sheetsDir, dark: false);
        WriteAnimStrip(duckBase, sheetsDir, dark: true);
        WriteAnimStrip(duckBase, sheetsDir, dark: false);
        WriteSizeLadder(duckBase, sheetsDir, dark: true);
        WriteSizeLadder(duckBase, sheetsDir, dark: false);
    }

    private static void WriteSheet(Bitmap duckBase, string sheetsDir, bool dark)
    {
        var bg = dark ? Color.FromArgb(255, 32, 32, 34) : Color.FromArgb(255, 240, 240, 242);
        var fg = dark ? Color.White : Color.Black;
        var sheet = new Bitmap(720, 280, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(bg);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using var font = new Font("Segoe UI", 11f);
        using var brush = new SolidBrush(fg);
        g.DrawString($"#129 tray health ring — 16px static (×4) — {(dark ? "dark" : "light")}", font, brush, 12, 10);

        var states = new[] { "healthy", "attention", "failed", "neutral" };
        for (var i = 0; i < states.Length; i++)
        {
            var geo = Geometries[0];
            using var icon = Compose(duckBase, geo, PaletteFor(states[i]), active: false, 0);
            var x = 24 + i * 170;
            g.DrawString(states[i], font, brush, x, 40);
            g.DrawImage(icon, x, 62, 64, 64);
            g.DrawImage(icon, x + 80, 78, 16, 16);
        }

        sheet.Save(Path.Combine(sheetsDir, dark ? "ring-static-16-dark.png" : "ring-static-16-light.png"), ImageFormat.Png);
        sheet.Dispose();
    }

    private static void WriteAnimStrip(Bitmap duckBase, string sheetsDir, bool dark)
    {
        var bg = dark ? Color.FromArgb(255, 32, 32, 34) : Color.FromArgb(255, 240, 240, 242);
        var cell = 56;
        var sheet = new Bitmap(cell * FrameCount + 24, cell + 40, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(bg);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using var font = new Font("Segoe UI", 10f);
        using var brush = new SolidBrush(dark ? Color.White : Color.Black);
        g.DrawString($"#129 healthy active 12f @16px×{(cell / 16)} — {(dark ? "dark" : "light")}", font, brush, 8, 6);
        var geo = Geometries[0];
        var palette = PaletteFor("healthy");
        for (var f = 0; f < FrameCount; f++)
        {
            using var icon = Compose(duckBase, geo, palette, active: true, f);
            g.DrawImage(icon, 12 + f * cell, 28, cell, cell);
        }

        sheet.Save(
            Path.Combine(sheetsDir, dark ? "ring-anim-healthy-16-dark.png" : "ring-anim-healthy-16-light.png"),
            ImageFormat.Png);
        sheet.Dispose();
    }

    private static void WriteSizeLadder(Bitmap duckBase, string sheetsDir, bool dark)
    {
        var bg = dark ? Color.FromArgb(255, 32, 32, 34) : Color.FromArgb(255, 240, 240, 242);
        var sheet = new Bitmap(640, 220, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(bg);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using var font = new Font("Segoe UI", 11f);
        using var brush = new SolidBrush(dark ? Color.White : Color.Black);
        g.DrawString($"#129 size ladder healthy static — {(dark ? "dark" : "light")}", font, brush, 12, 10);
        var palette = PaletteFor("healthy");
        var x = 20;
        foreach (var geo in Geometries)
        {
            using var icon = Compose(duckBase, geo, palette, active: false, 0);
            g.DrawString($"{geo.Size}px", font, brush, x, 40);
            var mag = Math.Max(4, 64 / geo.Size);
            g.DrawImage(icon, x, 62, geo.Size * mag, geo.Size * mag);
            g.DrawImage(icon, x, 62 + geo.Size * mag + 8, geo.Size, geo.Size);
            x += geo.Size * mag + 40;
        }

        sheet.Save(
            Path.Combine(sheetsDir, dark ? "ring-sizes-healthy-dark.png" : "ring-sizes-healthy-light.png"),
            ImageFormat.Png);
        sheet.Dispose();
    }
}
