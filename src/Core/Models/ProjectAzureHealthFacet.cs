namespace BuildMonitor.Core.Models;

/// <summary>One Azure pipeline run used for health and status presentation.</summary>
public sealed record AzurePipelineRunInfo(
    int DefinitionId,
    string PipelineDisplayName,
    long RunId,
    string BuildNumber,
    PipelineRunState State,
    PipelineRunResult Result,
    string Branch,
    DateTimeOffset QueuedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? RunUrl);

/// <summary>Project-level Azure health facet merged into <see cref="ProjectHealthSnapshot"/>.</summary>
public sealed record ProjectAzureHealthFacet(
    AzureMonitoringAvailability Availability,
    AzureCiMonitoringState CiState,
    string? FocusBranch,
    AzurePipelineRunInfo? PrimaryRun,
    IReadOnlyList<AzurePipelineRunInfo> AttentionRuns,
    DateTimeOffset PolledAtUtc,
    string? StatusMessage = null,
    bool HasSelectedPipelines = true);

/// <summary>Glanceable Azure block for the hover status panel.</summary>
public sealed record AzureStatusPresentation(
    bool ShowSection,
    string HeaderLabel,
    string Glyph,
    string PrimaryLine,
    string? SecondaryLine,
    string? AttentionLine,
    string? RunUrl,
    StatusPanelRowEmphasis Emphasis);
