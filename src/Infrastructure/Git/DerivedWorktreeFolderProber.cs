using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.Git;

/// <summary>
/// Cheap file-system probe for derived worktree staleness. Reads <c>.git</c> entries only;
/// never starts <c>git</c> and never modifies the folder.
/// </summary>
public static class DerivedWorktreeFolderProber
{
    private const string GitEntryName = ".git";
    private const string GitDirPrefix = "gitdir:";

    public static DerivedWorktreeFolderProbe Probe(string rootFolder)
    {
        if (string.IsNullOrWhiteSpace(rootFolder))
        {
            return new DerivedWorktreeFolderProbe(FolderExists: false, HasLiveGitWorktree: false);
        }

        try
        {
            var full = Path.GetFullPath(rootFolder);
            if (!Directory.Exists(full))
            {
                return new DerivedWorktreeFolderProbe(FolderExists: false, HasLiveGitWorktree: false);
            }

            return new DerivedWorktreeFolderProbe(FolderExists: true, HasLiveGitWorktree: HasLiveGitEntry(full));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Unreadable is not proof the worktree is gone; report it as healthy so it is never offered for removal.
            System.Diagnostics.Debug.WriteLine($"Derived worktree probe failed for '{rootFolder}': {ex.Message}");
            return new DerivedWorktreeFolderProbe(FolderExists: true, HasLiveGitWorktree: true);
        }
    }

    // Mirrors Git discovery: the nearest ancestor holding .git decides.
    private static bool HasLiveGitEntry(string folder)
    {
        for (var current = new DirectoryInfo(folder); current is not null; current = current.Parent)
        {
            var entry = Path.Combine(current.FullName, GitEntryName);
            if (Directory.Exists(entry))
            {
                return true;
            }

            if (File.Exists(entry))
            {
                return GitDirTargetExists(entry, current.FullName);
            }
        }

        return false;
    }

    private static bool GitDirTargetExists(string gitFile, string worktreeRoot)
    {
        var line = File.ReadLines(gitFile).FirstOrDefault()?.Trim();
        if (line is null || !line.StartsWith(GitDirPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var target = line[GitDirPrefix.Length..].Trim();
        if (target.Length == 0)
        {
            return false;
        }

        var resolved = Path.IsPathRooted(target) ? target : Path.Combine(worktreeRoot, target);
        return Directory.Exists(Path.GetFullPath(resolved));
    }
}
