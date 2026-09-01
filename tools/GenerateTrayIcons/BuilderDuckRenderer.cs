using System.Drawing;
using System.Drawing.Drawing2D;

namespace GenerateTrayIcons;

internal enum DuckBadgeKind
{
    None,
    Healthy,
    Building,
    Attention,
    Failed,
    Neutral
}

/// <summary>
/// Rasterises the approved builder-duck tray icon at multiple sizes.
/// Tuned for 16/20 px legibility — not cropped from the JPEG concept sheet.
/// </summary>
internal static class BuilderDuckRenderer
{
    private const int DesignSize = 32;

    private static readonly Color Outline = Color.FromArgb(255, 35, 35, 40);
    private static readonly Color DuckYellow = Color.FromArgb(255, 244, 196, 48);
    private static readonly Color HatYellow = Color.FromArgb(255, 255, 213, 79);
    private static readonly Color HatBrim = Color.FromArgb(255, 230, 180, 34);
    private static readonly Color BeakOrange = Color.FromArgb(255, 255, 140, 0);
    private static readonly Color EyeBlack = Color.FromArgb(255, 20, 20, 24);
    private static readonly Color EyeWhite = Color.FromArgb(255, 250, 250, 250);

    private static readonly Color BadgeHealthy = Color.FromArgb(255, 40, 180, 70);
    private static readonly Color BadgeBuilding = Color.FromArgb(255, 255, 130, 0);
    private static readonly Color BadgeAttention = Color.FromArgb(255, 255, 190, 0);
    private static readonly Color BadgeFailed = Color.FromArgb(255, 220, 60, 60);
    private static readonly Color BadgeNeutral = Color.FromArgb(255, 120, 120, 120);

    private static readonly Color NeutralDuck = Color.FromArgb(255, 170, 170, 170);
    private static readonly Color NeutralHat = Color.FromArgb(255, 150, 150, 150);
    private static readonly Color NeutralBeak = Color.FromArgb(255, 130, 130, 130);

    public static Bitmap Render(int pixelSize, DuckBadgeKind badge)
    {
        var bitmap = new Bitmap(pixelSize, pixelSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        var scale = pixelSize / (float)DesignSize;
        var duck = neutral ? NeutralDuck : DuckYellow;
        var hat = neutral ? NeutralHat : HatYellow;
        var brim = neutral ? NeutralHat : HatBrim;
        var beak = neutral ? NeutralBeak : BeakOrange;

        DrawHardHat(g, scale, hat, brim, neutral);
        DrawDuckHead(g, scale, duck);
        DrawBeak(g, scale, beak);
        DrawEye(g, scale, neutral);

        if (badge != DuckBadgeKind.None)
        {
            DrawBadge(g, scale, badge);
        }

        DrawOutlineStroke(g, scale);

        return bitmap;
    }

    private static void DrawHardHat(Graphics g, float scale, Color hatFill, Color brimFill, bool neutral)
    {
        float U(float u) => u * scale;
        var brimRect = new RectangleF(U(4), U(9.5f), U(24), U(4.5f));
        using var brimBrush = new SolidBrush(brimFill);
        g.FillEllipse(brimBrush, brimRect);

        var dome = new RectangleF(U(7), U(2.5f), U(18), U(10));
        using var hatBrush = new SolidBrush(hatFill);
        g.FillEllipse(hatBrush, dome);

        if (!neutral && scale >= 0.55f)
        {
            using var ridge = new Pen(Color.FromArgb(120, 255, 255, 255), Math.Max(1f, U(0.8f)));
            g.DrawArc(ridge, U(9), U(4), U(14), U(6), 200, 80);
        }
    }

    private static void DrawDuckHead(Graphics g, float scale, Color fill)
    {
        float U(float u) => u * scale;
        var head = new RectangleF(U(8.5f), U(11), U(19), U(17));
        using var brush = new SolidBrush(fill);
        g.FillEllipse(brush, head);
    }

    private static void DrawBeak(Graphics g, float scale, Color fill)
    {
        float U(float u) => u * scale;
        var points = new[]
        {
            new PointF(U(24), U(18)),
            new PointF(U(30), U(20.5f)),
            new PointF(U(24), U(23))
        };
        using var brush = new SolidBrush(fill);
        g.FillPolygon(brush, points);
    }

    private static void DrawEye(Graphics g, float scale, bool neutral)
    {
        float U(float u) => u * scale;
        var cx = U(18.5f);
        var cy = U(17.5f);
        var r = Math.Max(1.2f, U(pixelEyeRadius(scale)));

        using var black = new SolidBrush(neutral ? Color.FromArgb(255, 60, 60, 60) : EyeBlack);
        g.FillEllipse(black, cx - r, cy - r, r * 2f, r * 2f);

        if (scale >= 0.5f)
        {
            var hr = Math.Max(0.8f, r * 0.45f);
            using var white = new SolidBrush(EyeWhite);
            g.FillEllipse(white, cx - r * 0.15f, cy - r * 0.55f, hr, hr);
        }
    }

    private static float pixelEyeRadius(float scale) => scale < 0.55f ? 1.4f : 1.8f;

    private static void DrawBadge(Graphics g, float scale, DuckBadgeKind badge)
    {
        float U(float u) => u * scale;
        var color = badge switch
        {
            DuckBadgeKind.Healthy => BadgeHealthy,
            DuckBadgeKind.Building => BadgeBuilding,
            DuckBadgeKind.Attention => BadgeAttention,
            DuckBadgeKind.Failed => BadgeFailed,
            _ => BadgeNeutral
        };

        var diameter = U(pixelBadgeDiameter(scale));
        var x = U(1.5f);
        var y = U(DesignSize) - diameter - U(1.5f);

        using (var outline = new SolidBrush(Outline))
        {
            g.FillEllipse(outline, x - U(0.6f), y - U(0.6f), diameter + U(1.2f), diameter + U(1.2f));
        }

        using var fill = new SolidBrush(color);
        g.FillEllipse(fill, x, y, diameter, diameter);

        using var glyph = new Pen(Color.White, Math.Max(1f, diameter * 0.16f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        var cx = x + diameter / 2f;
        var cy = y + diameter / 2f;
        var s = diameter * 0.28f;

        switch (badge)
        {
            case DuckBadgeKind.Healthy:
                g.DrawLines(glyph, new[]
                {
                    new PointF(cx - s * 0.9f, cy + s * 0.1f),
                    new PointF(cx - s * 0.15f, cy + s * 0.85f),
                    new PointF(cx + s * 1.1f, cy - s * 0.75f)
                });
                break;
            case DuckBadgeKind.Building:
                g.DrawLine(glyph, cx - s * 0.2f, cy - s * 1.1f, cx - s * 0.2f, cy + s * 0.9f);
                g.DrawLine(glyph, cx - s * 1.0f, cy - s * 0.15f, cx + s * 0.85f, cy - s * 0.15f);
                g.DrawLine(glyph, cx + s * 0.85f, cy - s * 0.15f, cx + s * 0.85f, cy + s * 0.35f);
                break;
            case DuckBadgeKind.Attention:
                g.DrawLine(glyph, cx, cy - s * 1.0f, cx, cy + s * 0.35f);
                using (var dot = new SolidBrush(Color.White))
                {
                    var dr = Math.Max(1f, s * 0.45f);
                    g.FillEllipse(dot, cx - dr / 2f, cy + s * 0.55f, dr, dr);
                }

                break;
            case DuckBadgeKind.Failed:
                g.DrawLine(glyph, cx - s * 0.75f, cy - s * 0.75f, cx + s * 0.75f, cy + s * 0.75f);
                g.DrawLine(glyph, cx + s * 0.75f, cy - s * 0.75f, cx - s * 0.75f, cy + s * 0.75f);
                break;
            default:
                g.DrawLine(glyph, cx - s * 0.85f, cy, cx + s * 0.85f, cy);
                break;
        }
    }

    private static float pixelBadgeDiameter(float scale) =>
        scale switch
        {
            <= 0.55f => 10.5f,
            <= 0.65f => 10f,
            _ => 9.5f
        };

    private static void DrawOutlineStroke(Graphics g, float scale)
    {
        if (scale > 0.7f)
        {
            return;
        }

        float U(float u) => u * scale;
        using var pen = new Pen(Color.FromArgb(180, 20, 20, 24), Math.Max(1f, U(0.5f)));
        g.DrawEllipse(pen, U(8), U(11), U(19), U(17));
    }
}
