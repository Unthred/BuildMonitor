using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Abstractions;

/// <summary>Discover installed HTTP browsers via Windows registration (StartMenuInternet).</summary>
public interface IRegisteredBrowserCatalog
{
    IReadOnlyList<RegisteredBrowserDescriptor> GetBrowsers();

    void Refresh();

    bool TryResolve(string? registeredBrowserId, out RegisteredBrowserDescriptor? browser);
}

/// <summary>Launch http(s) URIs via system default or a registered browser executable.</summary>
public interface IHttpUriLauncher
{
    bool TryLaunch(Uri uri, RegisteredBrowserDescriptor? browser);
}

/// <summary>Opens project-scoped http(s) navigation using per-project browser preference.</summary>
public interface IProjectLinkLauncher
{
    void OpenHttpUri(string projectId, Uri uri);
}
