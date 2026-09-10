using System.Drawing;
using System.Windows.Threading;
using BuildMonitor.Core.Models;

namespace BuildMonitor.TrayApp.Services;

/// <summary>
/// Cycles pre-rendered active ring frames. Does not evaluate health — only advances the current presentation.
/// </summary>
internal sealed class TrayIconRingAnimator : IDisposable
{
    public const int FrameIntervalMilliseconds = 125;

    private readonly Action<Icon> applyIcon;
    private readonly Dispatcher dispatcher;
    private DispatcherTimer? timer;
    private TrayIconPresentation presentation = new(TrayHealthRing.Neutral, IsActive: false);
    private int frame;
    private bool disposed;

    public TrayIconRingAnimator(Action<Icon> applyIcon, Dispatcher? dispatcher = null)
    {
        this.applyIcon = applyIcon ?? throw new ArgumentNullException(nameof(applyIcon));
        this.dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public TrayIconPresentation CurrentPresentation => presentation;

    public int CurrentFrame => frame;

    public bool IsRunning => timer is { IsEnabled: true };

    /// <summary>Applies presentation from the normal health update path (not from timer ticks).</summary>
    public void SetPresentation(TrayIconPresentation next)
    {
        ThrowIfDisposed();
        var changed = next.Health != presentation.Health || next.IsActive != presentation.IsActive;
        presentation = next;
        if (changed)
        {
            frame = 0;
        }

        ApplyCurrentFrame();
        SyncTimer();
    }

    public void Stop()
    {
        if (disposed)
        {
            return;
        }

        StopTimer();
        if (presentation.IsAnimatable)
        {
            presentation = presentation with { IsActive = false };
        }

        ApplyCurrentFrame();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        StopTimer();
    }

    /// <summary>Test seam: advance one frame without a dispatcher timer.</summary>
    internal void TickForTests() => OnTick(null, EventArgs.Empty);

    private void SyncTimer()
    {
        if (!presentation.IsAnimatable)
        {
            StopTimer();
            return;
        }

        timer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(FrameIntervalMilliseconds),
            DispatcherPriority.Background,
            OnTick,
            dispatcher);
        timer.Interval = TimeSpan.FromMilliseconds(FrameIntervalMilliseconds);
        if (!timer.IsEnabled)
        {
            timer.Start();
        }
    }

    private void StopTimer()
    {
        if (timer is null)
        {
            return;
        }

        timer.Stop();
        timer.Tick -= OnTick;
        timer = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (disposed || !presentation.IsAnimatable)
        {
            StopTimer();
            return;
        }

        var next = (frame + 1) % TrayIconFactory.ActiveFrameCount;
        if (next == frame)
        {
            return;
        }

        frame = next;
        ApplyCurrentFrame();
    }

    private void ApplyCurrentFrame()
    {
        if (!TrayIconFactory.TryGetIcon(presentation, frame, out var icon) || icon is null)
        {
            icon = TrayIconFactory.GetStaticIcon(presentation.Health);
        }

        applyIcon(icon);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
