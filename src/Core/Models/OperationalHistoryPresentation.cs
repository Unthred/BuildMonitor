namespace BuildMonitor.Core.Models;

/// <summary>Whether recent-activity UI can read the in-memory operational history store.</summary>
public enum OperationalHistoryAvailability
{
    /// <summary>Store is available and returned zero events for this project.</summary>
    Empty = 0,
    /// <summary>Store returned one or more events.</summary>
    Available = 1,
    /// <summary>Store failed to initialize — history cannot be shown.</summary>
    Unavailable = 2
}

/// <summary>One compact timeline row derived from <see cref="OperationalEvent"/> (UI-facing).</summary>
public sealed record OperationalHistoryRowPresentation(
    string EventId,
    DateTimeOffset OccurredAtUtc,
    string TimeLabel,
    string SourceLabel,
    string SourceGlyph,
    string PrimaryText,
    string? SecondaryText,
    string? DetailText,
    string ToolTip,
    StatusPanelRowEmphasis Emphasis);

/// <summary>Recent-activity block for status cards or diagnostics.</summary>
public sealed record OperationalHistorySectionPresentation(
    OperationalHistoryAvailability Availability,
    IReadOnlyList<OperationalHistoryRowPresentation> Rows,
    bool ExpandByDefault,
    string HeaderLabel = "Recent activity",
    string EmptyMessage = "No recent activity yet",
    string UnavailableMessage = "Recent activity unavailable");
