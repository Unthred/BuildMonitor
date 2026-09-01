using System.Drawing;
using BuildMonitor.Core.Models;
using BuildMonitor.TrayApp.Services;

namespace BuildMonitor.Tests;

public sealed class TrayIconFactoryTests
{
    [Theory]
    [InlineData(TrayIconPresentationState.Neutral)]
    [InlineData(TrayIconPresentationState.Healthy)]
    [InlineData(TrayIconPresentationState.Building)]
    [InlineData(TrayIconPresentationState.Attention)]
    [InlineData(TrayIconPresentationState.Failed)]
    public void GetIcon_loads_committed_asset_for_each_state(TrayIconPresentationState state)
    {
        using var icon = TrayIconFactory.GetIcon(state);
        Assert.True(icon.Handle != IntPtr.Zero);
        Assert.True(icon.Size.Width >= 16);
    }

    [Fact]
    public void GetIcon_returns_same_cached_instance_for_steady_state()
    {
        var first = TrayIconFactory.GetIcon(TrayIconPresentationState.Healthy);
        var second = TrayIconFactory.GetIcon(TrayIconPresentationState.Healthy);
        Assert.Same(first, second);
    }
}
