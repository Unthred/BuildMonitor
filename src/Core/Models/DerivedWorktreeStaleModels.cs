namespace BuildMonitor.Core.Models;

/// <summary>Why a derived worktree registration looks finished.</summary>
public enum DerivedWorktreeStaleReason
{
    FolderMissing,
    NotGitWorktree,
    ParentMissing
}

/// <summary>File-system facts about a derived project's root folder.</summary>
/// <param name="FolderExists">The registered <c>rootFolder</c> exists.</param>
/// <param name="HasLiveGitWorktree">
/// The folder (or an ancestor) holds a <c>.git</c> directory, or a <c>.git</c> file whose
/// <c>gitdir:</c> target still exists.
/// </param>
public sealed record DerivedWorktreeFolderProbe(bool FolderExists, bool HasLiveGitWorktree);

public sealed record StaleDerivedWorktree(
    string ProjectId,
    string DisplayName,
    string RootFolder,
    DerivedWorktreeStaleReason Reason);

/// <summary>Result of a user-requested unregister of one stale derived worktree.</summary>
public sealed record StaleDerivedWorktreeUnregisterOutcome(
    StaleDerivedWorktree Worktree,
    bool Ok,
    string? Error);
