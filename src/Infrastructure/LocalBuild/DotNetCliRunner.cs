using System.Diagnostics;
using System.Text;

namespace BuildMonitor.Infrastructure.LocalBuild;

public sealed record CliRunResult(
    int ExitCode,
    string Output,
    TimeSpan Duration,
    string CommandLine,
    bool WasCancelled = false);

public sealed class DotNetCliRunner
{
    private readonly object processSync = new();
    private Process? activeProcess;

    public async Task<CliRunResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        Action<string>? onOutputLine = null,
        string? logBanner = null)
    {
        var commandLine = "dotnet " + string.Join(' ', arguments);
        var output = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(logBanner))
        {
            output.AppendLine(logBanner);
            output.AppendLine();
        }

        var started = DateTimeOffset.UtcNow;

        var psi = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var dotnetArgs = arguments.ToList();
        DotNetProcessConfigurator.Apply(psi, dotnetArgs);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        lock (processSync)
        {
            activeProcess = process;
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.AppendLine(e.Data);
                onOutputLine?.Invoke(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.AppendLine(e.Data);
                onOutputLine?.Invoke(e.Data);
            }
        };

        using var registration = cancellationToken.Register(() => TryKillActiveProcess(process));

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillActiveProcess(process);
            if (!process.HasExited)
            {
                try
                {
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Process may already be gone after kill.
                }
            }

            var cancelledDuration = DateTimeOffset.UtcNow - started;
            var cancelledText = BuildLogTextNormalizer.Normalize(
                BuildLogParser.DeduplicateConsecutiveLines(output.ToString()));
            return new CliRunResult(-1, cancelledText, cancelledDuration, commandLine, WasCancelled: true);
        }
        finally
        {
            lock (processSync)
            {
                if (ReferenceEquals(activeProcess, process))
                {
                    activeProcess = null;
                }
            }
        }

        var duration = DateTimeOffset.UtcNow - started;
        var text = BuildLogTextNormalizer.Normalize(
            BuildLogParser.DeduplicateConsecutiveLines(output.ToString()));
        return new CliRunResult(process.ExitCode, text, duration, commandLine);
    }

    private void TryKillActiveProcess(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort — caller handles cancellation outcome.
        }
    }
}
