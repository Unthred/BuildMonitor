using System.Windows.Threading;
using BuildMonitor.Infrastructure.Diagnostics;

namespace BuildMonitor.TrayApp.Services;

/// <summary>
/// Pings the WPF dispatcher from a thread-pool timer so UI stalls show up even when the debug window cannot repaint.
/// </summary>
public sealed class DispatcherHealthProbe : IDisposable
{
    private const string WorkerId = "ui.dispatcher";
    private const int PingIntervalMs = 250;
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMilliseconds(PingIntervalMs * 3);

    private readonly Dispatcher dispatcher;
    private readonly System.Threading.Timer timer;
    private long disposed;

    public DispatcherHealthProbe(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        WorkerHealthRegistry.Shared.Register(WorkerId, "WPF UI dispatcher", StaleAfter, "UI");
        timer = new System.Threading.Timer(Ping, null, PingIntervalMs, PingIntervalMs);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        timer.Dispose();
        WorkerHealthRegistry.Shared.Unregister(WorkerId);
    }

    private void Ping(object? _)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var op = dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
            {
                sw.Stop();
                WorkerHealthRegistry.Shared.SetCurrentAction("ui.dispatcher", "Dispatcher ping");
                WorkerHealthRegistry.Shared.Heartbeat(
                    WorkerId,
                    note: $"ping {sw.ElapsedMilliseconds} ms",
                    managedThreadId: Environment.CurrentManagedThreadId,
                    workDurationMs: sw.ElapsedMilliseconds);
                WorkerHealthRegistry.Shared.SetCurrentAction("ui.dispatcher", "Idle");
            });

            if (op.Wait(PingTimeout) != DispatcherOperationStatus.Completed)
            {
                WorkerHealthRegistry.Shared.RecordTimeout(WorkerId);
            }
        }
        catch (Exception ex)
        {
            WorkerHealthRegistry.Shared.Heartbeat(WorkerId, note: $"ping failed: {ex.Message}");
        }
    }
}
