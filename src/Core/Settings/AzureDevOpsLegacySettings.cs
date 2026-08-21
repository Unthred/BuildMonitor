using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Settings;

/// <summary>
/// Parked global ADO settings (unwired). Live schema uses
/// <see cref="AzureDevOpsConnectionSettings"/> and <see cref="AzureDevOpsProjectAttachment"/> (v21).
/// </summary>
public sealed class AzureDevOpsSettings
{
    public string OrganizationUrl { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public List<MonitoredPipelineSettings> Pipelines { get; init; } = [];
}

/// <summary>Parked per-pipeline settings for the unwired ADO module.</summary>
public sealed class MonitoredPipelineSettings
{
    public int PipelineId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public List<string> IncludedBranches { get; init; } = [];
    public int Priority { get; init; }
    public NotificationMode NotificationMode { get; init; } = NotificationMode.FailuresAndRecovery;
}
