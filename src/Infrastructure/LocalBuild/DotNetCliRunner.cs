using System.Diagnostics;
using System.Text;

namespace BuildMonitor.Infrastructure.LocalBuild;

public sealed record CliRunResult(
    int ExitCode,
    string Output,
    TimeSpan Duration,
    string CommandLine);

public sealed class DotNetCliRunner
{
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

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var duration = DateTimeOffset.UtcNow - started;
        var text = BuildLogTextNormalizer.Normalize(
            BuildLogParser.DeduplicateConsecutiveLines(output.ToString()));
        return new CliRunResult(process.ExitCode, text, duration, commandLine);
    }
}
