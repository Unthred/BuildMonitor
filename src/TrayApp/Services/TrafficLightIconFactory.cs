using System.Drawing;
using System.Drawing.Drawing2D;

namespace BuildMonitor.TrayApp.Services;

public static class TrafficLightIconFactory
{
    private const int IconSize = 32;

    private static readonly Dictionary<(Core.Models.MonitorHealth Health, int Frame), Icon> BuildingIconCache = new();

    private static Icon? greenIcon;
    private static Icon? amberIcon;
    private static Icon? redIcon;
    private static Icon? unknownIcon;

    public static Icon GetIcon(
        Core.Models.MonitorHealth health,
        bool isBuilding = false,
        int animationFrame = 0)
    {
        if (!isBuilding)
        {
            return GetBaseIcon(health);
        }

        var frame = ((animationFrame % 4) + 4) % 4;
        var key = (health, frame);
        if (BuildingIconCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        cached = CreateIcon(GetFillColor(health), isBuilding: true, frame);
        BuildingIconCache[key] = cached;
        return cached;
    }

    private static Icon GetBaseIcon(Core.Models.MonitorHealth health) =>
        health switch
        {
            Core.Models.MonitorHealth.Green => greenIcon ??= CreateIcon(GetFillColor(health)),
            Core.Models.MonitorHealth.Amber => amberIcon ??= CreateIcon(GetFillColor(health)),
            Core.Models.MonitorHealth.Red => redIcon ??= CreateIcon(GetFillColor(health)),
            _ => unknownIcon ??= CreateIcon(GetFillColor(health))
        };

    private static Color GetFillColor(Core.Models.MonitorHealth health) =>
        health switch
        {
            Core.Models.MonitorHealth.Green => Color.FromArgb(40, 167, 69),
            Core.Models.MonitorHealth.Amber => Color.FromArgb(255, 193, 7),
            Core.Models.MonitorHealth.Red => Color.FromArgb(220, 53, 69),
            _ => Color.FromArgb(108, 117, 125)
        };

    private static Icon CreateIcon(Color fill, bool isBuilding = false, int frame = 0)
    {
        using var bitmap = new Bitmap(IconSize, IconSize);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        var circleSize = isBuilding ? 22f : 28f;
        var circleOffset = isBuilding ? 1f : 2f;
        using var brush = new SolidBrush(fill);
        graphics.FillEllipse(brush, circleOffset, circleOffset, circleSize, circleSize);

        using var border = new Pen(Color.FromArgb(60, 60, 60), 1.5f);
        graphics.DrawEllipse(border, circleOffset, circleOffset, circleSize, circleSize);

        if (isBuilding)
        {
            DrawBuildingOverlay(graphics, frame);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static void DrawBuildingOverlay(Graphics graphics, int frame)
    {
        var swingAngle = frame switch
        {
            0 => -45f,
            1 => -15f,
            2 => 35f,
            _ => 5f
        };

        var state = graphics.Save();
        graphics.TranslateTransform(21f, 20f);
        graphics.RotateTransform(swingAngle);

        var handleRect = new RectangleF(-2.5f, 2f, 5f, 16f);
        var headRect = new RectangleF(-12f, -10f, 16f, 10f);

        using var outlinePen = new Pen(Color.FromArgb(255, 20, 20, 20), 1f)
        {
            Alignment = PenAlignment.Inset
        };

        using var handleBrush = new SolidBrush(Color.FromArgb(255, 210, 165, 70));
        graphics.FillRectangle(handleBrush, handleRect);
        graphics.DrawRectangle(outlinePen, handleRect.X, handleRect.Y, handleRect.Width, handleRect.Height);

        using var headBrush = new SolidBrush(Color.FromArgb(255, 210, 215, 225));
        graphics.FillRectangle(headBrush, headRect);
        graphics.DrawRectangle(outlinePen, headRect.X, headRect.Y, headRect.Width, headRect.Height);

        graphics.Restore(state);

        if (frame >= 2)
        {
            using var impact = new Pen(Color.FromArgb(230, 255, 255, 255), 2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(impact, 6f, 8f, 10f, 4f);
            graphics.DrawLine(impact, 6f, 12f, 11f, 12f);
            graphics.DrawLine(impact, 8f, 14f, 12f, 16f);
        }
    }
}
