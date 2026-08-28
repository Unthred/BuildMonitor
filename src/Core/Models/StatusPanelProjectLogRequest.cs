namespace BuildMonitor.Core.Models;

/// <summary>Status-panel request to open/reuse a project's BuildMonitor log viewer.</summary>
public sealed record StatusPanelProjectLogRequest(
    string ProjectId,
    bool SelectErrors = false,
    bool SelectWarnings = false);
