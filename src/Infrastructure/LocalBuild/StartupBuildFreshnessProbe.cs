using System.Diagnostics;
using System.Text;

namespace BuildMonitor.Infrastructure.LocalBuild;

/// <summary>
/// Reads the file-system facts <see cref="BuildMonitor.Core.Rules.StartupBuildPolicy"/> needs:
/// the newest meaningful source write and when the startup project's assembly was written.
/// Uses the same ignore rules as the file watcher, so anything that would not trigger a
/// File Watching rebuild does not force a startup build either.
/// </summary>
public static class StartupBuildFreshnessProbe
{
    public const string DefaultConfiguration = "Debug";

    private static readonly TimeSpan GitListTimeout = TimeSpan.FromSeconds(20);

    private static readonly EnumerationOptions TopLevelOnly = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    private static readonly EnumerationOptions Recursive = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    /// <summary>
    /// Latest last-write time (UTC) of any file under <paramref name="rootFolder"/> that the
    /// watcher treats as source, or null when none is found. In a Git working tree only files
    /// Git does not ignore count (tracked plus untracked-not-ignored), so outputs a build
    /// writes into the source tree, such as a gitignored generated script, do not make every
    /// build look stale. Outside Git, or when Git fails, the folder is walked instead.
    /// </summary>
    public static DateTimeOffset? FindNewestSourceWriteUtc(
        string rootFolder,
        IReadOnlySet<string> ignoreSegments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ignoreSegments);
        if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
        {
            return null;
        }

        var candidates = TryListGitSourceFiles(rootFolder, cancellationToken)
            ?? WalkSourceFiles(rootFolder, ignoreSegments, cancellationToken);
        return NewestWrite(candidates, rootFolder, ignoreSegments, cancellationToken);
    }

    /// <summary>
    /// Oldest last-write time (UTC) of <c>&lt;ProjectName&gt;.dll</c> under
    /// <c>bin/&lt;configuration&gt;</c> next to the startup project, or null when there is none.
    /// Only the configuration the host runs from counts, so a leftover Release ship-check copy
    /// cannot make a current Debug output look stale (or the reverse). With several target
    /// frameworks the oldest copy is used. A custom assembly name or output path reports null
    /// and the project is simply built.
    /// </summary>
    public static DateTimeOffset? FindBuildOutputWrittenUtc(string startupProjectPath, string configuration)
    {
        if (string.IsNullOrWhiteSpace(startupProjectPath) || string.IsNullOrWhiteSpace(configuration))
        {
            return null;
        }

        var projectDirectory = Path.GetDirectoryName(startupProjectPath);
        if (string.IsNullOrEmpty(projectDirectory))
        {
            return null;
        }

        var configurationDirectory = Path.Combine(projectDirectory, "bin", configuration);
        if (!Directory.Exists(configurationDirectory))
        {
            return null;
        }

        var assemblyFileName = Path.GetFileNameWithoutExtension(startupProjectPath) + ".dll";
        DateTime? oldest = null;
        foreach (var assembly in Directory.EnumerateFiles(configurationDirectory, assemblyFileName, Recursive))
        {
            var written = File.GetLastWriteTimeUtc(assembly);
            if (oldest is null || written < oldest)
            {
                oldest = written;
            }
        }

        return ToUtcOffset(oldest);
    }

    /// <summary>
    /// The build configuration named in the project's extra dotnet arguments
    /// (<c>-c X</c>, <c>-c:X</c>, <c>--configuration X</c>, <c>--configuration=X</c>), else Debug.
    /// </summary>
    public static string ResolveConfiguration(string? extraDotNetArgs)
    {
        if (string.IsNullOrWhiteSpace(extraDotNetArgs))
        {
            return DefaultConfiguration;
        }

        var tokens = extraDotNetArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? configuration = null;
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token is "-c" or "--configuration")
            {
                if (i + 1 < tokens.Length)
                {
                    configuration = tokens[++i];
                }
            }
            else if (TryValueAfterPrefix(token, "-c:", out var value)
                || TryValueAfterPrefix(token, "--configuration=", out value)
                || TryValueAfterPrefix(token, "--configuration:", out value))
            {
                configuration = value;
            }
        }

        return string.IsNullOrWhiteSpace(configuration) ? DefaultConfiguration : configuration;
    }

    private static bool TryValueAfterPrefix(string token, string prefix, out string value)
    {
        if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && token.Length > prefix.Length)
        {
            value = token[prefix.Length..];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static DateTimeOffset? NewestWrite(
        IEnumerable<string> files,
        string rootFolder,
        IReadOnlySet<string> ignoreSegments,
        CancellationToken cancellationToken)
    {
        DateTime? newest = null;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(rootFolder, file);
            if (WatchIgnoreRules.ShouldIgnorePath(relative, ignoreSegments) || !File.Exists(file))
            {
                continue;
            }

            var written = File.GetLastWriteTimeUtc(file);
            if (newest is null || written > newest)
            {
                newest = written;
            }
        }

        return ToUtcOffset(newest);
    }

    /// <summary>
    /// Full paths of files Git does not ignore, or null when <paramref name="rootFolder"/> is not
    /// in a Git working tree or Git cannot be run. Paths are relative to the folder Git runs in.
    /// </summary>
    private static List<string>? TryListGitSourceFiles(string rootFolder, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = rootFolder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "ls-files", "-z", "--cached", "--others", "--exclude-standard" })
        {
            psi.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            if (!process.WaitForExit(GitListTimeout))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            _ = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                return null;
            }

            return stdout
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(relative => Path.GetFullPath(Path.Combine(rootFolder, relative)))
                .ToList();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Git is not installed or could not start: fall back to walking the folder.
            Debug.WriteLine($"git ls-files failed in {rootFolder}: {ex.Message}");
            return null;
        }
    }

    private static IEnumerable<string> WalkSourceFiles(
        string rootFolder,
        IReadOnlySet<string> ignoreSegments,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(rootFolder);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            foreach (var child in Directory.EnumerateDirectories(directory, "*", TopLevelOnly))
            {
                if (!ignoreSegments.Contains(Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", TopLevelOnly))
            {
                yield return file;
            }
        }
    }

    private static DateTimeOffset? ToUtcOffset(DateTime? utc) =>
        utc is null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc));
}
