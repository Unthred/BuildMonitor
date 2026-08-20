namespace BuildMonitor.Core.Rules;

/// <summary>
/// Pure geometry for deciding whether a window is sufficiently on-screen and clamping it into a work area.
/// </summary>
public static class WindowScreenVisibility
{
    /// <summary>Minimum fraction of window area that must intersect work areas to count as visible.</summary>
    public const double MinVisibleAreaFraction = 0.5;

    public readonly record struct Rect(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;
        public double CenterX => X + (Width / 2.0);
        public double CenterY => Y + (Height / 2.0);
        public double Area => Math.Max(0, Width) * Math.Max(0, Height);
    }

    /// <summary>
    /// True when the window center lies in any work area, or at least
    /// <see cref="MinVisibleAreaFraction"/> of the window area intersects the union of work areas.
    /// </summary>
    public static bool IsSufficientlyVisible(Rect window, IReadOnlyList<Rect> workAreas)
    {
        if (workAreas.Count == 0 || window.Width <= 0 || window.Height <= 0)
        {
            return false;
        }

        if (workAreas.Any(area => ContainsPoint(area, window.CenterX, window.CenterY)))
        {
            return true;
        }

        var windowArea = window.Area;
        if (windowArea <= 0)
        {
            return false;
        }

        var visible = 0.0;
        foreach (var area in workAreas)
        {
            visible += IntersectionArea(window, area);
        }

        return visible / windowArea >= MinVisibleAreaFraction;
    }

    /// <summary>
    /// Places <paramref name="window"/> fully inside <paramref name="workArea"/>, shrinking only when larger than the area.
    /// </summary>
    public static Rect ClampToWorkArea(Rect window, Rect workArea)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            return window;
        }

        var width = Math.Min(Math.Max(1, window.Width), workArea.Width);
        var height = Math.Min(Math.Max(1, window.Height), workArea.Height);
        var maxX = workArea.Right - width;
        var maxY = workArea.Bottom - height;
        var x = double.IsFinite(window.X) ? Math.Clamp(window.X, workArea.X, Math.Max(workArea.X, maxX)) : workArea.X;
        var y = double.IsFinite(window.Y) ? Math.Clamp(window.Y, workArea.Y, Math.Max(workArea.Y, maxY)) : workArea.Y;
        return new Rect(x, y, width, height);
    }

    /// <summary>
    /// Prefers <paramref name="preferredWorkArea"/> when provided; otherwise the work area whose center is nearest the window center.
    /// </summary>
    public static Rect ResolveTargetWorkArea(
        Rect window,
        IReadOnlyList<Rect> workAreas,
        Rect? preferredWorkArea = null)
    {
        if (preferredWorkArea is { Width: > 0, Height: > 0 } preferred)
        {
            return preferred;
        }

        if (workAreas.Count == 0)
        {
            return new Rect(0, 0, 1920, 1080);
        }

        if (workAreas.Count == 1)
        {
            return workAreas[0];
        }

        Rect best = workAreas[0];
        var bestDistance = double.PositiveInfinity;
        foreach (var area in workAreas)
        {
            var dx = area.CenterX - window.CenterX;
            var dy = area.CenterY - window.CenterY;
            var distance = (dx * dx) + (dy * dy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = area;
            }
        }

        return best;
    }

    /// <summary>
    /// If the window is not sufficiently visible, clamp it into the resolved target work area; otherwise return unchanged.
    /// </summary>
    public static Rect EnsureVisible(Rect window, IReadOnlyList<Rect> workAreas, Rect? preferredWorkArea = null)
    {
        if (IsSufficientlyVisible(window, workAreas))
        {
            return window;
        }

        var target = ResolveTargetWorkArea(window, workAreas, preferredWorkArea);
        return ClampToWorkArea(window, target);
    }

    private static bool ContainsPoint(Rect area, double x, double y) =>
        x >= area.X && x < area.Right && y >= area.Y && y < area.Bottom;

    private static double IntersectionArea(Rect a, Rect b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        return width * height;
    }
}
