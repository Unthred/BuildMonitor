using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Settings;

/// <summary>
/// Optional Azure DevOps module settings (parked; not used by local-build MVP).
/// </summary>
public sealed class AzureDevOpsSettings
{
    public string OrganizationUrl { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public List<MonitoredPipelineSettings> Pipelines { get; init; } = [];
}

public sealed class MonitoredPipelineSettings
{
    public int PipelineId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public List<string> IncludedBranches { get; init; } = [];
    public int Priority { get; init; }
    public NotificationMode NotificationMode { get; init; } = NotificationMode.FailuresAndRecovery;
}
