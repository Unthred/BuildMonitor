namespace BuildMonitor.Core.Models;

/// <summary>
/// A Windows StartMenuInternet-registered HTTP browser discovered at runtime.
/// Executable path is resolved from registry — never persisted in settings.
/// </summary>
public sealed record RegisteredBrowserDescriptor(
    string RegisteredBrowserId,
    string DisplayName,
    string ExecutablePath);
