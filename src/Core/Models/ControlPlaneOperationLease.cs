namespace BuildMonitor.Core.Models;

/// <summary>Kind of exclusive control-plane <c>/run/*</c> work owned by a lease.</summary>
public enum ControlPlaneOperationKind
{
    Rebuild = 0,
    Tests = 1,
    ShipCheck = 2
}

/// <summary>
/// Authoritative owner of one in-flight control-plane operation (id + cancellation).
/// History may correlate to <see cref="OperationId"/> but never owns control flow.
/// </summary>
public sealed class ControlPlaneOperationLease : IDisposable
{
    private int cancelRequested;
    private int disposed;

    public ControlPlaneOperationLease(ControlPlaneOperationKind kind)
    {
        Kind = kind;
        OperationId = Guid.NewGuid().ToString("N");
        StartedAtUtc = DateTimeOffset.UtcNow;
        Cancellation = new CancellationTokenSource();
    }

    public string OperationId { get; }

    public ControlPlaneOperationKind Kind { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public CancellationTokenSource Cancellation { get; }

    public CancellationToken Token => Cancellation.Token;

    public bool CancelRequested => Volatile.Read(ref cancelRequested) != 0;

    public bool RequestCancel()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return false;
        }

        var already = Interlocked.Exchange(ref cancelRequested, 1) != 0;
        try
        {
            Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Lease already released.
        }

        return already;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            Cancellation.Dispose();
        }
        catch
        {
            // ignore
        }
    }
}
