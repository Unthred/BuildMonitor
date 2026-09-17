namespace BuildMonitor.Core.Models;

/// <summary>
/// Immutable identity captured at the start of a build/start/restart operation.
/// Must be passed through the full asynchronous lifetime so later callbacks cannot
/// re-read UI selection, list index, or mutated settings for a different project.
/// </summary>
public sealed record ProjectRunContext(
    string ProjectId,
    string DisplayName,
    string RootFolder,
    string StartupProjectPath,
    string WorkingDirectory,
    string? LaunchProfile,
    string LaunchSettingsPath,
    string? ApplicationUrlOverride,
    IReadOnlyList<string> ProfileListenUrls,
    string? EffectiveApplicationUrl,
    string ExtraDotNetArgs);
