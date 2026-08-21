using System.Diagnostics;
using System.Text;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Infrastructure.Git;

public sealed class LocalGitContextReader : ILocalGitContextReader
{
    public async Task<LocalGitContext> ReadAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot) || !Directory.Exists(repositoryRoot))
        {
            return new LocalGitContext(LocalGitHeadStatus.Unavailable, null, [], "Repository folder not found.");
        }

        try
        {
            var head = (await RunGitAsync(repositoryRoot, "rev-parse --abbrev-ref HEAD", cancellationToken)).Trim();
            var remotesRaw = await RunGitAsync(repositoryRoot, "remote -v", cancellationToken);
            var remotes = ParseRemotes(remotesRaw);

            if (string.Equals(head, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                return new LocalGitContext(LocalGitHeadStatus.Detached, null, remotes, "Detached HEAD");
            }

            if (string.IsNullOrWhiteSpace(head))
            {
                return new LocalGitContext(LocalGitHeadStatus.Unavailable, null, remotes, "Could not read HEAD.");
            }

            var branch = AzureGitBranchNormalizer.ToShortName(head) ?? head;
            return new LocalGitContext(LocalGitHeadStatus.Branch, branch, remotes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new LocalGitContext(LocalGitHeadStatus.Unavailable, null, [], ex.Message);
        }
    }

    private static IReadOnlyList<LocalGitRemote> ParseRemotes(string output)
    {
        var list = new List<LocalGitRemote>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var name = parts[0];
            var url = parts[1];
            var key = name + "|" + url;
            if (!seen.Add(key))
            {
                continue;
            }

            list.Add(new LocalGitRemote(name, url));
        }

        return list;
    }

    private static async Task<string> RunGitAsync(string workingDirectory, string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start git.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? $"git exited {process.ExitCode}." : stderr.Trim());
        }

        return stdout;
    }
}
