using System.Diagnostics;
using System.Text;

namespace BuildMonitor.Infrastructure.LocalBuild;

public sealed class SupervisedProcess : IDisposable
{
    private readonly StringBuilder output = new();
    private Process? process;

    public string ProjectId { get; }
    public string CommandLine { get; private set; } = string.Empty;
    public bool IsRunning => process is { HasExited: false };
    public string Output => output.ToString();

    public event Action<string, int>? Exited;
    public event Action<string>? OutputLineReceived;

    public SupervisedProcess(string projectId) => ProjectId = projectId;

    public void Start(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        Action<ProcessStartInfo>? configure = null)
    {
        Stop();

        CommandLine = "dotnet " + string.Join(' ', arguments);
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var dotnetArgs = arguments.ToList();
        DotNetProcessConfigurator.Apply(psi, dotnetArgs, forLongRunningHost: true);
        configure?.Invoke(psi);

        process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var runningProcess = process;
        runningProcess.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (output)
                {
                    output.AppendLine(e.Data);
                }

                OutputLineReceived?.Invoke(e.Data);
            }
        };
        runningProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (output)
                {
                    output.AppendLine(e.Data);
                }

                OutputLineReceived?.Invoke(e.Data);
            }
        };
        runningProcess.Exited += (_, _) =>
        {
            try
            {
                if (!runningProcess.HasExited)
                {
                    return;
                }

                var code = runningProcess.ExitCode;
                Exited?.Invoke(ProjectId, code);
            }
            catch (InvalidOperationException)
            {
                // process disposed during shutdown
            }
        };

        runningProcess.Start();
        runningProcess.BeginOutputReadLine();
        runningProcess.BeginErrorReadLine();
    }

    public void Stop()
    {
        if (process is null)
        {
            return;
        }

        try
        {
            ProcessTerminationHelper.StopGracefully(process);
        }
        catch
        {
            // ignore shutdown races
        }
        finally
        {
            process.Dispose();
            process = null;
        }
    }

    public async Task StopGracefullyAsync(CancellationToken cancellationToken = default)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            await ProcessTerminationHelper.StopGracefullyAsync(process, cancellationToken: cancellationToken);
        }
        catch
        {
            // ignore shutdown races
        }
        finally
        {
            process.Dispose();
            process = null;
        }
    }

    public void Dispose() => Stop();
}
