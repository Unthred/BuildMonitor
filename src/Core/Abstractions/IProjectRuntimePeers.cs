namespace BuildMonitor.Core.Abstractions;

/// <summary>
/// Sibling-project facts needed to keep run/listen isolation per configured project.
/// Never keyed by list index or "currently selected" UI project.
/// </summary>
public sealed record PeerProjectListenInfo(
    string ProjectId,
    string DisplayName,
    string RootFolder,
    IReadOnlyList<string> ListenUrls,
    bool IsRunProcessActive);

public interface IProjectRuntimePeers
{
    IReadOnlyList<PeerProjectListenInfo> GetPeers(string projectId);

    void PersistApplicationUrl(string projectId, string applicationUrl);
}
