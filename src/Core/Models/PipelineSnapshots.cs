namespace BuildMonitor.Core.Models;

public sealed record StageSnapshot(
    string StageName,
    PipelineRunState State,
    PipelineRunResult Result,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? Url);

public sealed record PipelineSnapshot(
    int PipelineId,
    string PipelineName,
    long RunId,
    string RunName,
    PipelineRunState State,
    PipelineRunResult Result,
    string Branch,
    string? CommitSha,
    string? RequestedBy,
    DateTimeOffset QueuedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string RunUrl,
    IReadOnlyList<StageSnapshot> Stages);

public sealed record MonitorSnapshot(
    DateTimeOffset PolledAtUtc,
    IReadOnlyList<PipelineSnapshot> Pipelines);
