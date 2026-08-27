using System.Threading;

namespace BuildMonitor.Infrastructure.ControlPlane;

public enum AppQuitClaimResult
{
    Accepted,
    AlreadyInProgress
}

/// <summary>
/// Thread-safe accept + failsafe ordering for <c>POST /app/quit</c> and tray Exit.
/// Failsafe must be armed before any UI work that can throw on a non-UI thread.
/// </summary>
public static class AppQuitLifecycle
{
    public static AppQuitClaimResult TryClaim(ref int exitRequestedFlag) =>
        Interlocked.Exchange(ref exitRequestedFlag, 1) == 0
            ? AppQuitClaimResult.Accepted
            : AppQuitClaimResult.AlreadyInProgress;

    /// <summary>
    /// Arms the hard-exit failsafe, then schedules graceful teardown.
    /// Exceptions from <paramref name="scheduleGracefulExit"/> must not undo the failsafe.
    /// </summary>
    public static void ArmFailsafeThenScheduleGraceful(
        Action armFailsafe,
        Action scheduleGracefulExit)
    {
        ArgumentNullException.ThrowIfNull(armFailsafe);
        ArgumentNullException.ThrowIfNull(scheduleGracefulExit);
        armFailsafe();
        scheduleGracefulExit();
    }

    /// <summary>
    /// Invokes the tray quit callback for HTTP <c>/app/quit</c>.
    /// Returns false when unavailable or when the callback throws (maps to HTTP 503, not 500).
    /// </summary>
    public static bool TryInvokeQuitCallback(Action? requestAppQuit)
    {
        if (requestAppQuit is null)
        {
            return false;
        }

        try
        {
            requestAppQuit();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>Classifies HTTP outcomes for deploy/quit callers (mirrors Deploy-BuildMonitor.ps1).</summary>
public enum AppQuitHttpDisposition
{
    /// <summary>202 — quit accepted; wait for process/control-plane exit.</summary>
    Accepted,

    /// <summary>404/503 — quit endpoint unavailable; do not treat as already stopped.</summary>
    Unavailable,

    /// <summary>Connection refused / no response — tray likely already down.</summary>
    AlreadyDown,

    /// <summary>5xx or other unexpected status — tray may still be holding binaries.</summary>
    ServerError
}

public static class AppQuitHttpDispositionClassifier
{
    public static AppQuitHttpDisposition Classify(int? httpStatusCode, bool transportFailed)
    {
        if (httpStatusCode is 202)
        {
            return AppQuitHttpDisposition.Accepted;
        }

        if (httpStatusCode is 404 or 503)
        {
            return AppQuitHttpDisposition.Unavailable;
        }

        if (transportFailed || httpStatusCode is null)
        {
            return AppQuitHttpDisposition.AlreadyDown;
        }

        if (httpStatusCode >= 500)
        {
            return AppQuitHttpDisposition.ServerError;
        }

        // Unexpected 2xx/3xx/4xx — treat as failure so deploy does not continue blindly.
        return AppQuitHttpDisposition.ServerError;
    }
}
