namespace BuildMonitor.Core.Models;

public enum LocalGitHeadStatus
{
    Branch,
    Detached,
    Unavailable
}

public sealed record LocalGitRemote(string Name, string Url);

public sealed record LocalGitContext(
    LocalGitHeadStatus HeadStatus,
    string? CurrentBranch,
    IReadOnlyList<LocalGitRemote> Remotes,
    string? Detail = null);
