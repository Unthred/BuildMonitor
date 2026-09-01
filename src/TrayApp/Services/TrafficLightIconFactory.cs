using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using BuildMonitor.Core.Models;

namespace BuildMonitor.TrayApp.Services;

/// <summary>
/// Legacy programmatic traffic-light tray icons. Superseded by <see cref="TrayIconFactory"/> (#95).
/// Retained for one release; remove after mascot icons are visually accepted.
/// </summary>
[Obsolete("Replaced by TrayIconFactory and committed builder-duck assets (#95).")]
public static class TrafficLightIconFactory
{
    private const int DesignSize = 32;

    private static readonly int[] TrayIconSizes = [16, 20, 24, 32];

    private static readonly Color RedBright = Color.FromArgb(255, 235, 65, 80);
    private static readonly Color AmberBright = Color.FromArgb(255, 255, 205, 0);
    private static readonly Color GreenBright = Color.FromArgb(255, 50, 205, 90);

    private static readonly Color RedMuted = Blend(Color.FromArgb(48, 24, 28), RedBright, 0.42f);
    private static readonly Color AmberMuted = Blend(Color.FromArgb(48, 40, 20), AmberBright, 0.42f);
    private static readonly Color GreenMuted = Blend(Color.FromArgb(24, 40, 28), GreenBright, 0.42f);

    private static readonly Color Housing = Color.FromArgb(255, 48, 48, 52);
    private static readonly Color HousingBorder = Color.FromArgb(255, 28, 28, 30);

    private static readonly Dictionary<(MonitorHealth Health, bool IsBuilding, int Frame, bool Showcase, bool WebReady), Icon> IconCache = new();

    private static Icon? showcaseIcon;

    public static Icon GetShowcaseIcon() =>
        showcaseIcon ??= CreateTrafficLightIcon(MonitorHealth.Unknown, isBuilding: false, animationFrame: 0, showcaseAllLamps: true);

    public static Icon GetIcon(
        MonitorHealth health,
        bool isBuilding = false,
        int animationFrame = 0,
        bool webReady = false)
    {
        if (!isBuilding)
        {
            var steadyKey = (health, false, 0, false, webReady);
            if (IconCache.TryGetValue(steadyKey, out var steady))
            {
                return steady;
            }

            steady = CreateTrafficLightIcon(health, isBuilding: false, animationFrame: 0, showcaseAllLamps: false, webReady);
            IconCache[steadyKey] = steady;
            return steady;
        }

        var frame = ((animationFrame % 4) + 4) % 4;
        var key = (health, true, frame, false, webReady);
        if (IconCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        cached = CreateTrafficLightIcon(health, isBuilding: true, animationFrame: frame, showcaseAllLamps: false, webReady);
        IconCache[key] = cached;
        return cached;
    }

    private static Icon CreateTrafficLightIcon(
        MonitorHealth health,
        bool isBuilding,
        int animationFrame,
        bool showcaseAllLamps,
        bool webReady = false)
    {
        var bitmaps = TrayIconSizes
            .Select(size => RenderBitmap(size, health, isBuilding, animationFrame, showcaseAllLamps, webReady))
            .ToArray();

        try
        {
            return CreateMultiSizeIcon(bitmaps);
        }
        finally
        {
            foreach (var bitmap in bitmaps)
            {
                bitmap.Dispose();
            }
        }
    }

    private static Bitmap RenderBitmap(
        int pixelSize,
        MonitorHealth health,
        bool isBuilding,
        int animationFrame,
        bool showcaseAllLamps,
        bool webReady)
    {
        var bitmap = new Bitmap(pixelSize, pixelSize, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.Clear(Color.Transparent);

        var scale = pixelSize / (float)DesignSize;
        float U(float units) => units * scale;

        var housing = new RectangleF(U(0), U(0), U(32), U(32));
        var corner = Math.Max(1f, U(6));
        FillRoundedRectangle(graphics, new SolidBrush(Housing), housing, corner);
        using (var housingBorder = new Pen(HousingBorder, Math.Max(1f, U(1f))))
        {
            DrawRoundedRectangle(graphics, housingBorder, housing, corner);
        }

        var activeLamp = showcaseAllLamps ? TrafficLamp.None : MapHealthToLamp(health);

        DrawLamp(graphics, TrafficLamp.Red, activeLamp, isBuilding, animationFrame, showcaseAllLamps, scale);
        DrawLamp(graphics, TrafficLamp.Amber, activeLamp, isBuilding, animationFrame, showcaseAllLamps, scale);
        DrawLamp(graphics, TrafficLamp.Green, activeLamp, isBuilding, animationFrame, showcaseAllLamps, scale);

        if (webReady && !showcaseAllLamps)
        {
            var badgeLamp = activeLamp == TrafficLamp.None ? TrafficLamp.Green : activeLamp;
            DrawWebReadyBadge(graphics, scale, badgeLamp);
        }

        return bitmap;
    }

    private static float LampCenterY(TrafficLamp lamp, Func<float, float> unit) =>
        lamp switch
        {
            TrafficLamp.Red => unit(7.5f),
            TrafficLamp.Amber => unit(16f),
            _ => unit(24.5f)
        };

    private static void DrawWebReadyBadge(Graphics graphics, float scale, TrafficLamp activeLamp)
    {
        float U(float units) => units * scale;

        var badgeSize = U(7.5f);
        var lampDiameter = U(14.5f);
        var lampCenterX = U(DesignSize / 2f);
        var lampCenterY = LampCenterY(activeLamp, U);
        var x = lampCenterX + lampDiameter / 2f - badgeSize + U(0.5f);
        var y = lampCenterY + lampDiameter / 2f - badgeSize + U(0.5f);

        using var outer = new SolidBrush(Color.FromArgb(255, 255, 255));
        graphics.FillEllipse(outer, x - U(0.75f), y - U(0.75f), badgeSize + U(1.5f), badgeSize + U(1.5f));

        using var fill = new SolidBrush(Color.FromArgb(255, 62, 127, 207));
        graphics.FillEllipse(fill, x, y, badgeSize, badgeSize);

        using var globePen = new Pen(Color.FromArgb(230, 255, 255, 255), Math.Max(1f, U(0.85f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        var cx = x + badgeSize / 2f;
        var cy = y + badgeSize / 2f;
        var rx = badgeSize * 0.28f;
        var ry = badgeSize * 0.34f;
        graphics.DrawEllipse(globePen, cx - rx, cy - ry, rx * 2f, ry * 2f);
        graphics.DrawLine(globePen, cx - rx, cy, cx + rx, cy);
        graphics.DrawArc(globePen, cx - rx * 0.55f, cy - ry, rx * 1.1f, ry * 2f, 90, 180);
    }

    private static void DrawLamp(
        Graphics graphics,
        TrafficLamp lamp,
        TrafficLamp activeLamp,
        bool isBuilding,
        int animationFrame,
        bool showcaseAllLamps,
        float scale)
    {
        float U(float units) => units * scale;

        var centerY = LampCenterY(lamp, U);

        var isActive = showcaseAllLamps || activeLamp == lamp;
        var (muted, bright) = GetLampColors(lamp);
        var fill = ResolveLampFill(muted, bright, isActive, isBuilding && isActive, animationFrame);

        var diameter = isActive ? U(14.5f) : U(12.5f);
        var x = U(DesignSize / 2f) - diameter / 2f;
        var y = centerY - diameter / 2f;

        using var brush = new SolidBrush(fill);
        graphics.FillEllipse(brush, x, y, diameter, diameter);

        if (isActive)
        {
            using var bloom = new SolidBrush(Color.FromArgb(70, bright));
            var bloomPad = U(2.2f);
            graphics.FillEllipse(bloom, x - bloomPad, y - bloomPad, diameter + bloomPad * 2f, diameter + bloomPad * 2f);
            graphics.FillEllipse(brush, x, y, diameter, diameter);
        }

        using var rim = new Pen(Color.FromArgb(isActive ? 200 : 90, 0, 0, 0), Math.Max(1f, U(0.85f)));
        graphics.DrawEllipse(rim, x, y, diameter, diameter);

        if (isActive)
        {
            var glowWidth = isBuilding && animationFrame % 2 == 0 ? U(2.25f) : U(1.5f);
            using var highlight = new Pen(Color.FromArgb(240, 255, 255, 255), Math.Max(1f, glowWidth));
            var glowPad = U(1.4f);
            graphics.DrawEllipse(highlight, x - glowPad, y - glowPad, diameter + glowPad * 2f, diameter + glowPad * 2f);
        }
    }

    private static Icon CreateMultiSizeIcon(IReadOnlyList<Bitmap> bitmaps)
    {
        var pngImages = new byte[bitmaps.Count][];
        for (var i = 0; i < bitmaps.Count; i++)
        {
            using var pngStream = new MemoryStream();
            bitmaps[i].Save(pngStream, ImageFormat.Png);
            pngImages[i] = pngStream.ToArray();
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)pngImages.Length);

        var offset = 6 + 16 * pngImages.Length;
        for (var i = 0; i < bitmaps.Count; i++)
        {
            WriteIconDirectoryEntry(writer, bitmaps[i].Width, bitmaps[i].Height, pngImages[i].Length, offset);
            offset += pngImages[i].Length;
        }

        foreach (var png in pngImages)
        {
            writer.Write(png);
        }

        stream.Position = 0;
        return new Icon(stream);
    }

    private static void WriteIconDirectoryEntry(BinaryWriter writer, int width, int height, int dataSize, int offset)
    {
        writer.Write((byte)(width >= 256 ? 0 : width));
        writer.Write((byte)(height >= 256 ? 0 : height));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(dataSize);
        writer.Write(offset);
    }

    private static (Color Muted, Color Bright) GetLampColors(TrafficLamp lamp) =>
        lamp switch
        {
            TrafficLamp.Red => (RedMuted, RedBright),
            TrafficLamp.Amber => (AmberMuted, AmberBright),
            _ => (GreenMuted, GreenBright)
        };

    private static TrafficLamp MapHealthToLamp(MonitorHealth health) =>
        health switch
        {
            MonitorHealth.Red => TrafficLamp.Red,
            MonitorHealth.Amber => TrafficLamp.Amber,
            MonitorHealth.Green => TrafficLamp.Green,
            _ => TrafficLamp.None
        };

    private static Color ResolveLampFill(
        Color muted,
        Color bright,
        bool isActive,
        bool pulse,
        int animationFrame)
    {
        if (!isActive)
        {
            return muted;
        }

        if (!pulse)
        {
            return bright;
        }

        var strength = animationFrame is 0 or 2 ? 1f : 0.82f;
        return Blend(muted, bright, strength);
    }

    private static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        var r = (int)(from.R + (to.R - from.R) * amount);
        var g = (int)(from.G + (to.G - from.G) * amount);
        var b = (int)(from.B + (to.B - from.B) * amount);
        return Color.FromArgb(255, r, g, b);
    }

    private static void FillRoundedRectangle(Graphics graphics, Brush brush, RectangleF bounds, float radius)
    {
        using var path = CreateRoundedRectPath(bounds, radius);
        graphics.FillPath(brush, path);
    }

    private static void DrawRoundedRectangle(Graphics graphics, Pen pen, RectangleF bounds, float radius)
    {
        using var path = CreateRoundedRectPath(bounds, radius);
        graphics.DrawPath(pen, path);
    }

    private static GraphicsPath CreateRoundedRectPath(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2f;
        if (diameter > bounds.Width)
        {
            diameter = bounds.Width;
        }

        if (diameter > bounds.Height)
        {
            diameter = bounds.Height;
        }

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private enum TrafficLamp
    {
        None,
        Red,
        Amber,
        Green
    }
}
