using System.Drawing;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.TrayApp.Services;

namespace BuildMonitor.Tests;

public sealed class TrayIconRingAnimatorTests
{
    [Fact]
    public void Active_presentation_starts_and_advances_frame_without_new_health_eval()
    {
        TrayIconFactory.ClearCacheForTests();
        var applied = new List<Icon>();
        using var animator = new TrayIconRingAnimator(applied.Add);
        var presentation = new TrayIconPresentation(TrayHealthRing.Healthy, IsActive: true);
        animator.SetPresentation(presentation);
        Assert.Equal(0, animator.CurrentFrame);
        Assert.Single(applied);

        animator.TickForTests();
        Assert.Equal(1, animator.CurrentFrame);
        Assert.Equal(2, applied.Count);
        Assert.NotSame(applied[0], applied[1]);
        Assert.Equal(presentation, animator.CurrentPresentation);
    }

    [Fact]
    public void Idle_stops_animation()
    {
        TrayIconFactory.ClearCacheForTests();
        using var animator = new TrayIconRingAnimator(_ => { });
        animator.SetPresentation(new TrayIconPresentation(TrayHealthRing.Healthy, IsActive: true));
        Assert.True(animator.IsRunning);
        animator.SetPresentation(new TrayIconPresentation(TrayHealthRing.Healthy, IsActive: false));
        Assert.False(animator.IsRunning);
        Assert.Equal(0, animator.CurrentFrame);
    }

    [Fact]
    public void Transition_to_Failed_stops_animation_immediately()
    {
        TrayIconFactory.ClearCacheForTests();
        var applied = new List<Icon>();
        using var animator = new TrayIconRingAnimator(applied.Add);
        animator.SetPresentation(new TrayIconPresentation(TrayHealthRing.Healthy, IsActive: true));
        animator.TickForTests();
        Assert.True(animator.CurrentFrame > 0);

        animator.SetPresentation(new TrayIconPresentation(TrayHealthRing.Failed, IsActive: true));
        Assert.False(animator.IsRunning);
        Assert.Equal(0, animator.CurrentFrame);
        Assert.Same(TrayIconFactory.GetStaticIcon(TrayHealthRing.Failed), applied[^1]);
    }

    [Fact]
    public void Transition_Failed_to_healthy_active_starts_animation()
    {
        TrayIconFactory.ClearCacheForTests();
        using var animator = new TrayIconRingAnimator(_ => { });
        animator.SetPresentation(new TrayIconPresentation(TrayHealthRing.Failed, IsActive: true));
        Assert.False(animator.IsRunning);
        animator.SetPresentation(new TrayIconPresentation(TrayHealthRing.Healthy, IsActive: true));
        Assert.True(animator.IsRunning);
        Assert.Equal(0, animator.CurrentFrame);
    }

    [Fact]
    public void Stop_disables_timer_and_falls_back_to_static()
    {
        TrayIconFactory.ClearCacheForTests();
        var applied = new List<Icon>();
        using var animator = new TrayIconRingAnimator(applied.Add);
        animator.SetPresentation(new TrayIconPresentation(TrayHealthRing.Attention, IsActive: true));
        animator.Stop();
        Assert.False(animator.IsRunning);
        Assert.Same(TrayIconFactory.GetStaticIcon(TrayHealthRing.Attention), applied[^1]);
    }
}

public sealed class TrayIconAnimationRegressionTests
{
    [Fact]
    public void Activity_presentation_is_stable_for_identical_snapshots()
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
        Assert.Equal(TrayHealthRing.Attention, first.Health);
        Assert.True(first.IsAnimatable);
        Assert.Equal(first, second);
    }
}
