using System.ComponentModel;
using System.Diagnostics;

namespace BuildMonitor.Infrastructure.LocalBuild;

public sealed record ProcessStopResult(bool Success, string? Error);

public static class ProcessTerminationHelper
{
    private static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(2);

    public static async Task<ProcessStopResult> TryStopGracefullyAsync(
        Process process,
        TimeSpan? gracePeriod = null,
        CancellationToken cancellationToken = default)
    {
        if (process.HasExited)
        {
            return new ProcessStopResult(true, null);
        }

        var grace = gracePeriod ?? DefaultGracePeriod;

        try
        {
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                _ = process.CloseMainWindow();
            }
        }
        catch (Exception ex)
        {
            return new ProcessStopResult(false, DescribeFailure(process, "Could not request close", ex));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(grace);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new ProcessStopResult(true, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // grace period elapsed; fall through to kill
        }
        catch (Exception ex)
        {
            return new ProcessStopResult(false, DescribeFailure(process, "Wait for exit failed", ex));
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }

            return process.HasExited
                ? new ProcessStopResult(true, null)
                : new ProcessStopResult(false, DescribeFailure(process, "Process did not exit", null));
        }
        catch (Exception ex)
        {
            return new ProcessStopResult(false, DescribeFailure(process, "Kill failed", ex));
        }
    }

    public static void StopGracefully(Process process, TimeSpan? gracePeriod = null)
    {
        TryStopGracefullyAsync(process, gracePeriod).GetAwaiter().GetResult();
    }

    public static async Task StopGracefullyAsync(
        Process process,
        TimeSpan? gracePeriod = null,
        CancellationToken cancellationToken = default)
    {
        _ = await TryStopGracefullyAsync(process, gracePeriod, cancellationToken);
    }

    private static string DescribeFailure(Process process, string action, Exception? ex)
    {
        var identity = $"{process.ProcessName} (PID {process.Id})";
        if (ex is Win32Exception win32)
        {
            return $"{action} for {identity}: {win32.Message} (Win32 {win32.NativeErrorCode})";
        }

        if (ex is UnauthorizedAccessException)
        {
            return $"{action} for {identity}: access denied — close this app manually before rebuilding";
        }

        return ex is null
            ? $"{action} for {identity}"
            : $"{action} for {identity}: {ex.Message}";
    }
}
