namespace BuildMonitor.Core.Rules;

/// <summary>
/// Schema v25 adds optional <c>local.derivedFromProjectId</c> so control-plane
/// register/unregister can mark and safely remove derived Git worktree projects
/// without touching manually configured siblings.
/// </summary>
public static class SettingsSchemaV25
{
    public const int Version = 25;
}
