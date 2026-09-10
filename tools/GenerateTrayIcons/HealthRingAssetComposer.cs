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

    // Opaque muted tracks (not low-alpha) so AA cannot paint a soft coloured disc.
    private static readonly Color Green = Color.FromArgb(255, 40, 180, 70);
    private static readonly Color GreenBright = Color.FromArgb(255, 70, 220, 100);
    private static readonly Color GreenTrack = Color.FromArgb(255, 28, 110, 48);
    private static readonly Color Amber = Color.FromArgb(255, 230, 170, 20);
    private static readonly Color AmberBright = Color.FromArgb(255, 255, 200, 40);
    private static readonly Color AmberTrack = Color.FromArgb(255, 150, 110, 16);
    private static readonly Color Red = Color.FromArgb(255, 220, 60, 60);
    private static readonly Color Grey = Color.FromArgb(255, 140, 140, 145);
    private static readonly Color GreyBright = Color.FromArgb(255, 190, 190, 195);
    private static readonly Color GreyTrack = Color.FromArgb(255, 90, 90, 95);

    private sealed record RingPalette(Color Solid, Color Track, Color Arc);

    /// <summary>
    /// Duck inset keeps mascot size; ring sits on the outer canvas edge (max diameter).
    /// Stroke is integer pixels painted as a hard annulus (no GDI AA).
    /// </summary>
    private sealed record SizeGeometry(int Size, int Stroke, int DuckInset);

    private static readonly SizeGeometry[] Geometries =
    [
        // Ring at canvas limit under option A still only ~1px gap. Option B: +1 inset for breathing room.
        new(16, Stroke: 1, DuckInset: 3),
        new(20, Stroke: 1, DuckInset: 4),
        new(24, Stroke: 2, DuckInset: 4),
        new(32, Stroke: 2, DuckInset: 5)
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

        using var duckRaw = new Bitmap(duckBasePath);
        using var duckBase = ToTransparentDuck(duckRaw);
        AssertCornersTransparent(duckBase, duckBasePath);

        foreach (var health in new[] { "healthy", "attention", "failed", "neutral" })
        {
            var palette = PaletteFor(health);
            var staticFrames = new List<Bitmap>();
            foreach (var geo in Geometries)
            {
                var bmp = Compose(duckBase, geo, palette, active: false, frame: 0);
                AssertCornersTransparent(bmp, $"tray-{health}-{geo.Size}.png");
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
                    AssertCornersTransparent(bmp, $"tray-{health}-a{frame:00}-{geo.Size}.png");
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
        WriteAlphaInspectionSheet(duckBase, sheetsDir);
        Console.WriteLine($"Health-ring assets written ({4 + 3 * FrameCount} ICOs).");
        Console.WriteLine(
            "16px geometry: duck inset=3 (10×10, option B), hard 1px annulus at canvas limit (outerR≈8).");
    }

    private static RingPalette PaletteFor(string health) =>
        health switch
        {
            "healthy" => new RingPalette(Green, GreenTrack, GreenBright),
            "attention" => new RingPalette(Amber, AmberTrack, AmberBright),
            "failed" => new RingPalette(Red, Red, Red),
            _ => new RingPalette(Grey, GreyTrack, GreyBright)
        };

    /// <summary>
    /// Source duck is often 24bpp RGB on white/checker. Convert to 32bpp with true transparency.
    /// Does not alter opaque duck/hat/beak colours.
    /// </summary>
    internal static Bitmap ToTransparentDuck(Bitmap source)
    {
        var dst = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var c = source.GetPixel(x, y);
                if (IsBackgroundPixel(c))
                {
                    dst.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0));
                }
                else
                {
                    dst.SetPixel(x, y, Color.FromArgb(255, c.R, c.G, c.B));
                }
            }
        }

        return dst;
    }

    private static bool IsBackgroundPixel(Color c)
    {
        // Near-white / light grey canvas (24bpp export) and near-black matte.
        if (c.R >= 245 && c.G >= 245 && c.B >= 245)
        {
            return true;
        }

        if (c.R <= 12 && c.G <= 12 && c.B <= 12)
        {
            return true;
        }

        // Checkerboard light cell often ~204–230 grey when flattened.
        var max = Math.Max(c.R, Math.Max(c.G, c.B));
        var min = Math.Min(c.R, Math.Min(c.G, c.B));
        if (max - min <= 8 && min >= 190)
        {
            return true;
        }

        return false;
    }

    private static Bitmap Compose(
        Bitmap duckBase,
        SizeGeometry geo,
        RingPalette palette,
        bool active,
        int frame)
    {
        var bmp = new Bitmap(geo.Size, geo.Size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(0, 0, 0, 0));
            g.CompositingMode = CompositingMode.SourceOver;
            g.CompositingQuality = CompositingQuality.HighQuality;

            // Draw duck at target size directly (HQ downscale once from transparent source).
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            var duckBox = new Rectangle(
                geo.DuckInset,
                geo.DuckInset,
                geo.Size - geo.DuckInset * 2,
                geo.Size - geo.DuckInset * 2);
            g.DrawImage(duckBase, duckBox);
        }

        ScrubResidualBackground(bmp);
        // Hard pixel ring last so soft duck fringe cannot muddy the track.
        PaintCrispRing(bmp, geo, palette, active, frame);
        return bmp;
    }

    /// <summary>
    /// Opaque annulus painted by distance test — no GDI pens, no AA, no glow.
    /// Outer radius uses the canvas limit so the ring sits on the outermost usable pixels.
    /// </summary>
    private static void PaintCrispRing(
        Bitmap bmp,
        SizeGeometry geo,
        RingPalette palette,
        bool active,
        int frame)
    {
        var cx = (geo.Size - 1) / 2f;
        var cy = cx;
        // Just under size/2 so mid-edge pixels (0, mid) / (mid, 0) are included without clipping.
        var outerR = geo.Size / 2f - 0.01f;
        var innerR = outerR - geo.Stroke;
        var arcStart = -90f + 360f * frame / FrameCount;

        for (var y = 0; y < geo.Size; y++)
        {
            for (var x = 0; x < geo.Size; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                var d = MathF.Sqrt(dx * dx + dy * dy);
                if (d <= innerR || d > outerR)
                {
                    continue;
                }

                // Y-down Atan2 matches GDI+ clockwise arcs from +X.
                var angle = MathF.Atan2(dy, dx) * 180f / MathF.PI;
                if (!active)
                {
                    bmp.SetPixel(x, y, palette.Solid);
                    continue;
                }

                bmp.SetPixel(
                    x,
                    y,
                    AngleInSweep(angle, arcStart, ArcDegrees) ? palette.Arc : palette.Track);
            }
        }
    }

    private static bool AngleInSweep(float angleDeg, float startDeg, float sweepDeg)
    {
        var a = angleDeg;
        while (a < startDeg)
        {
            a += 360f;
        }

        while (a >= startDeg + 360f)
        {
            a -= 360f;
        }

        return a >= startDeg && a < startDeg + sweepDeg;
    }

    private static void ScrubResidualBackground(Bitmap bmp)
    {
        for (var y = 0; y < bmp.Height; y++)
        {
            for (var x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.A == 0)
                {
                    if (c.R != 0 || c.G != 0 || c.B != 0)
                    {
                        bmp.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0));
                    }

                    continue;
                }

                // Drop near-invisible bicubic fringe (reads as haze / false green on dark trays).
                if (c.A < 48)
                {
                    bmp.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0));
                    continue;
                }

                // Soft light fringe from bicubic downscale of former white matte.
                if (c.A < 220 && c.R >= 200 && c.G >= 200 && c.B >= 200)
                {
                    bmp.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0));
                    continue;
                }

                if (c.A < 120 && c.R >= 200 && c.G >= 180 && c.B >= 120)
                {
                    bmp.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0));
                    continue;
                }

                if (c.R >= 250 && c.G >= 250 && c.B >= 250 && c.A < 255)
                {
                    bmp.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0));
                    continue;
                }

                // Harden surviving duck coverage (no semi-transparent haze in the tray).
                if (c.A < 255)
                {
                    bmp.SetPixel(x, y, Color.FromArgb(255, c.R, c.G, c.B));
                }
            }
        }
    }

    internal static void AssertCornersTransparent(Bitmap bmp, string label)
    {
        ReadOnlySpan<(int X, int Y)> corners =
        [
            (0, 0),
            (bmp.Width - 1, 0),
            (0, bmp.Height - 1),
            (bmp.Width - 1, bmp.Height - 1)
        ];

        foreach (var (x, y) in corners)
        {
            var c = bmp.GetPixel(x, y);
            if (c.A != 0)
            {
                throw new InvalidOperationException(
                    $"Corner ({x},{y}) not transparent on {label}: A={c.A} RGB={c.R},{c.G},{c.B}");
            }
        }
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

    private static void WriteAlphaInspectionSheet(Bitmap duckBase, string sheetsDir)
    {
        // Magenta checker behind icons makes accidental opaque background obvious.
        var sheet = new Bitmap(520, 200, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        DrawChecker(g, sheet.Width, sheet.Height);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using var font = new Font("Segoe UI", 11f);
        using var brush = new SolidBrush(Color.Black);
        g.DrawString("#129 alpha inspection — magenta checker (corners must show through)", font, brush, 8, 8);

        var geo = Geometries[0];
        using var idle = Compose(duckBase, geo, PaletteFor("healthy"), active: false, 0);
        using var active = Compose(duckBase, geo, PaletteFor("healthy"), active: true, 0);
        g.DrawImage(idle, 20, 40, 64, 64);
        g.DrawImage(idle, 100, 56, 16, 16);
        g.DrawImage(active, 160, 40, 64, 64);
        g.DrawImage(active, 240, 56, 16, 16);
        sheet.Save(Path.Combine(sheetsDir, "ring-alpha-inspection.png"), ImageFormat.Png);
        sheet.Dispose();
    }

    private static void DrawChecker(Graphics g, int w, int h)
    {
        const int cell = 8;
        using var a = new SolidBrush(Color.FromArgb(255, 255, 0, 255));
        using var b = new SolidBrush(Color.FromArgb(255, 40, 40, 40));
        for (var y = 0; y < h; y += cell)
        {
            for (var x = 0; x < w; x += cell)
            {
                g.FillRectangle(((x / cell) + (y / cell)) % 2 == 0 ? a : b, x, y, cell, cell);
            }
        }
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
