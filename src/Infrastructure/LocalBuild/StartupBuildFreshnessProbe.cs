namespace BuildMonitor.Infrastructure.LocalBuild;

/// <summary>
/// Reads the file-system facts <see cref="BuildMonitor.Core.Rules.StartupBuildPolicy"/> needs:
/// the newest meaningful source write and whether the startup project's assembly exists.
/// Uses the same ignore rules as the file watcher, so anything that would not trigger a
/// File Watching rebuild does not force a startup build either.
/// </summary>
public static class StartupBuildFreshnessProbe
{
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
    /// watcher treats as source. Excluded directories are not walked. Null when none found.
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

        DateTime? newest = null;
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
                if (WatchIgnoreRules.IsNoiseFile(file))
                {
                    continue;
                }

                var written = File.GetLastWriteTimeUtc(file);
                if (newest is null || written > newest)
                {
                    newest = written;
                }
            }
        }

        return newest is null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(newest.Value, DateTimeKind.Utc));
    }

    /// <summary>
    /// Oldest last-write time (UTC) of <c>&lt;ProjectName&gt;.dll</c> under <c>bin</c> next to the
    /// startup project, or null when there is none. The oldest copy is used so that one fresh
    /// configuration (for example a Release ship-check) cannot hide a stale Debug output.
    /// A project with a custom assembly name or output path reports null and is simply built.
    /// </summary>
    public static DateTimeOffset? FindBuildOutputWrittenUtc(string startupProjectPath)
    {
        if (string.IsNullOrWhiteSpace(startupProjectPath))
        {
            return null;
        }

        var projectDirectory = Path.GetDirectoryName(startupProjectPath);
        if (string.IsNullOrEmpty(projectDirectory))
        {
            return null;
        }

        var binDirectory = Path.Combine(projectDirectory, "bin");
        if (!Directory.Exists(binDirectory))
        {
            return null;
        }

        var assemblyFileName = Path.GetFileNameWithoutExtension(startupProjectPath) + ".dll";
        DateTime? oldest = null;
        foreach (var assembly in Directory.EnumerateFiles(binDirectory, assemblyFileName, Recursive))
        {
            var written = File.GetLastWriteTimeUtc(assembly);
            if (oldest is null || written < oldest)
            {
                oldest = written;
            }
        }

        return oldest is null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(oldest.Value, DateTimeKind.Utc));
    }
}
