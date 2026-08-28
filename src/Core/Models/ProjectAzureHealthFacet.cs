namespace BuildMonitor.Core.Models;

/// <summary>One Azure pipeline run used for health and status presentation.</summary>
public sealed record AzurePipelineRunInfo(
    int DefinitionId,
    string PipelineDisplayName,
    long RunId,
    string? BuildNumber,
    PipelineRunState State,
    PipelineRunResult Result,
    string Branch,
    DateTimeOffset QueuedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? RunUrl,
    int? PullRequestNumber = null,
    /// <summary>Real source branch ref for navigation; never a PR merge ref when avoidable.</summary>
    string? SourceBranchRef = null);

/// <summary>Project-level Azure health facet merged into <see cref="ProjectHealthSnapshot"/>.</summary>
public sealed record ProjectAzureHealthFacet(
    AzureMonitoringAvailability Availability,
    AzureCiMonitoringState CiState,
    string? FocusBranch,
    AzurePipelineRunInfo? PrimaryRun,
    IReadOnlyList<AzurePipelineRunInfo> AttentionRuns,
    DateTimeOffset PolledAtUtc,
    string? StatusMessage = null,
    bool HasSelectedPipelines = true,
    AzureBuildNavigationContext? NavigationContext = null);

/// <summary>One compact Azure table row in the hover status panel.</summary>
public sealed record AzureStatusTableRow(
    string Pipeline,
    string StatusGlyph,
    string StatusText,
    string Branch,
    string RunDisplay,
    string BuildNumberDisplay,
    string PullRequestDisplay,
    string? TimingText,
    string? RunUrl,
    StatusPanelRowEmphasis Emphasis);

/// <summary>Glanceable Azure block for the hover status panel.</summary>
public sealed record AzureStatusPresentation(
    bool ShowSection,
    string HeaderLabel,
    bool ShowTable,
    string? MessageGlyph,
    string? MessagePrimary,
    string? MessageSecondary,
    IReadOnlyList<AzureStatusTableRow> Rows,
    string? AttentionLine,
    string? PrimaryRunUrl,
    StatusPanelRowEmphasis Emphasis);
