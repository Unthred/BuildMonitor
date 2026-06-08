namespace BuildMonitor.Core.Models;

public sealed record PipelineTransitionKey(
    int PipelineId,
    long RunId,
    string? StageName,
    PipelineRunResult Result);

public sealed record MonitoringEvent(
    PipelineTransitionKey Key,
    string Title,
    string Message,
    MonitorHealth Health,
    bool IsRecovery,
    string DeepLinkUrl);
