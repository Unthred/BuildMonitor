using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class TrayIconAnimationRegressionTests
{
    [Fact]
    public void Building_state_is_static_presentation_not_animation_frames()
    {
        var active = new[]
        {
            new ProjectHealthSnapshot(
                "p1",
                "Demo",
                MonitorHealth.Amber,
                "Building",
                ProjectLifecycleState.Building,
                null,
                null,
                null,
                0,
                0,
                DateTimeOffset.UtcNow,
                null,
                true,
                [])
        };

        var first = TrayIconPresentationMapper.Resolve(active);
        var second = TrayIconPresentationMapper.Resolve(active);
        Assert.Equal(TrayIconPresentationState.Building, first);
        Assert.Equal(first, second);
    }
}
