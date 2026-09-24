using System.Diagnostics;
using System.Text;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Infrastructure.Git;

/// <summary>Reads Git worktree identity via absolute <c>--git-common-dir</c>.</summary>
public interface IGitWorktreeIdentityReader
{
    Task<GitWorktreeIdentityInfo?> TryReadAsync(string path, CancellationToken cancellationToken);
}

public sealed class GitWorktreeIdentityReader : IGitWorktreeIdentityReader
{
    public async Task<GitWorktreeIdentityInfo?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return null;
        }

        try
        {
            var inside = (await RunGitAsync(path, "rev-parse --is-inside-work-tree", cancellationToken)
                .ConfigureAwait(false)).Trim();
            if (!string.Equals(inside, "true", StringComparison.OrdinalIgnoreCase))
            {
                return new GitWorktreeIdentityInfo(false, string.Empty);
            }

            var commonDirRaw = (await RunGitAsync(
                    path,
                    "rev-parse --path-format=absolute --git-common-dir",
                    cancellationToken)
                .ConfigureAwait(false)).Trim();
            if (string.IsNullOrWhiteSpace(commonDirRaw))
            {
                return new GitWorktreeIdentityInfo(true, string.Empty);
            }

            var commonDir = GitWorktreeIdentity.NormalizeCommonDir(
                Path.IsPathRooted(commonDirRaw)
                    ? commonDirRaw
                    : Path.Combine(path, commonDirRaw));
            return new GitWorktreeIdentityInfo(true, commonDir);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<string> RunGitAsync(
        string workingDirectory,
        string arguments,
        CancellationToken cancellationToken)
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
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr)
                    ? $"git exited {process.ExitCode}."
                    : stderr.Trim());
        }

        return stdout;
    }
}
