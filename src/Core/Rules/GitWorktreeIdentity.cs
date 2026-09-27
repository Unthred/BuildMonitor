namespace BuildMonitor.Core.Rules;

/// <summary>
/// Compare Git repository identity via absolute <c>--git-common-dir</c> paths.
/// Folder naming alone is never sufficient.
/// </summary>
public static class GitWorktreeIdentity
{
    public static string NormalizeCommonDir(string? commonDir)
    {
        if (string.IsNullOrWhiteSpace(commonDir))
        {
            return string.Empty;
        }

        return Path.GetFullPath(commonDir.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool SameRepository(string? commonDirA, string? commonDirB)
    {
        var left = NormalizeCommonDir(commonDirA);
        var right = NormalizeCommonDir(commonDirB);
        if (left.Length == 0 || right.Length == 0)
        {
            return false;
        }

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Facts required to validate a path as a same-repository Git worktree.</summary>
public sealed record GitWorktreeIdentityInfo(bool IsInsideWorkTree, string CommonDirAbsolute);
