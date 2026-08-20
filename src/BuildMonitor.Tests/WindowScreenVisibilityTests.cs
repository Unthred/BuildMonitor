using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class WindowScreenVisibilityTests
{
    private static WindowScreenVisibility.Rect Primary => new(0, 0, 1920, 1080);
    private static WindowScreenVisibility.Rect Secondary => new(1920, 0, 1920, 1080);

    [Fact]
    public void IsSufficientlyVisible_true_when_center_on_primary()
    {
        var window = new WindowScreenVisibility.Rect(100, 100, 800, 600);
        Assert.True(WindowScreenVisibility.IsSufficientlyVisible(window, [Primary]));
    }

    [Fact]
    public void IsSufficientlyVisible_false_when_fully_on_missing_secondary()
    {
        var window = new WindowScreenVisibility.Rect(2200, 100, 800, 600);
        Assert.False(WindowScreenVisibility.IsSufficientlyVisible(window, [Primary]));
    }

    [Fact]
    public void IsSufficientlyVisible_true_on_secondary_when_present()
    {
        var window = new WindowScreenVisibility.Rect(2200, 100, 800, 600);
        Assert.True(WindowScreenVisibility.IsSufficientlyVisible(window, [Primary, Secondary]));
    }

    [Fact]
    public void IsSufficientlyVisible_false_for_one_pixel_edge_overlap()
    {
        // Almost entirely off to the right of a single 1920-wide primary; only 1px still intersects.
        var window = new WindowScreenVisibility.Rect(1919, 100, 800, 600);
        Assert.False(WindowScreenVisibility.IsSufficientlyVisible(window, [Primary]));
    }

    [Fact]
    public void IsSufficientlyVisible_true_when_majority_overlaps_even_if_center_off()
    {
        // Center sits on the right edge (not inside primary); half the width still overlaps.
        var window = new WindowScreenVisibility.Rect(960, 100, 1920, 600);
        Assert.Equal(1920, window.CenterX);
        Assert.True(WindowScreenVisibility.IsSufficientlyVisible(window, [Primary]));
    }

    [Fact]
    public void ClampToWorkArea_moves_offscreen_window_fully_inside()
    {
        var window = new WindowScreenVisibility.Rect(2500, 100, 800, 600);
        var clamped = WindowScreenVisibility.ClampToWorkArea(window, Primary);
        Assert.Equal(1120, clamped.X); // 1920 - 800
        Assert.Equal(100, clamped.Y);
        Assert.Equal(800, clamped.Width);
        Assert.Equal(600, clamped.Height);
        Assert.True(WindowScreenVisibility.IsSufficientlyVisible(clamped, [Primary]));
    }

    [Fact]
    public void ClampToWorkArea_shrinks_when_larger_than_work_area()
    {
        var window = new WindowScreenVisibility.Rect(0, 0, 3000, 2000);
        var clamped = WindowScreenVisibility.ClampToWorkArea(window, Primary);
        Assert.Equal(0, clamped.X);
        Assert.Equal(0, clamped.Y);
        Assert.Equal(1920, clamped.Width);
        Assert.Equal(1080, clamped.Height);
    }

    [Fact]
    public void EnsureVisible_leaves_on_screen_window_unchanged()
    {
        var window = new WindowScreenVisibility.Rect(100, 100, 800, 600);
        var result = WindowScreenVisibility.EnsureVisible(window, [Primary]);
        Assert.Equal(window, result);
    }

    [Fact]
    public void EnsureVisible_uses_preferred_work_area_when_clamping()
    {
        // Secondary coords with only primary still present (RDP / unplug).
        var window = new WindowScreenVisibility.Rect(2500, 100, 400, 300);
        var preferred = new WindowScreenVisibility.Rect(100, 100, 800, 600);
        var result = WindowScreenVisibility.EnsureVisible(window, [Primary], preferred);
        Assert.True(result.X >= preferred.X);
        Assert.True(result.Right <= preferred.Right);
        Assert.True(result.Y >= preferred.Y);
        Assert.True(result.Bottom <= preferred.Bottom);
    }

    [Fact]
    public void ResolveTargetWorkArea_picks_nearest_when_no_preferred()
    {
        var window = new WindowScreenVisibility.Rect(2500, 100, 400, 300);
        var target = WindowScreenVisibility.ResolveTargetWorkArea(window, [Primary, Secondary]);
        Assert.Equal(Secondary, target);
    }
}
