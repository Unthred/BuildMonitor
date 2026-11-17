using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// User-facing Overall footer labels from composite project health.
/// Amber activity must not surface as "Warnings": the headline stays "Attention" and
/// <see cref="StatusPanelOverallSummary.Reason"/> says why (#167).
/// </summary>
public static class StatusPanelOverallFormatter
{
    public const string NotStartedLabel = "Not started";

    private const string AttentionLabel = "Attention";
    private const string SignInRequiredReason = "sign-in required";
    private const string CiUnreachableReason = "CI unreachable";
    private const string CiPartialReason = "CI partially succeeded";

    public static string FormatLabel(
        MonitorHealth overallHealth,
        IReadOnlyList<ProjectHealthSnapshot> activeSnapshots) =>
        Summarize(overallHealth, activeSnapshots).Label;

    /// <summary>
    /// Headline label plus, for Attention, the cause (short, for the footer and rail) and a
    /// per-project tooltip.
    /// </summary>
    public static StatusPanelOverallSummary Summarize(
        MonitorHealth overallHealth,
        IReadOnlyList<ProjectHealthSnapshot> activeSnapshots)
    {
        ArgumentNullException.ThrowIfNull(activeSnapshots);
        return overallHealth switch
        {
            MonitorHealth.Red => new StatusPanelOverallSummary("Needs fix"),
            MonitorHealth.Green => new StatusPanelOverallSummary("Healthy"),
            MonitorHealth.Amber => SummarizeAmber(activeSnapshots),
            _ => SummarizeUnknown(activeSnapshots)
        };
    }

    /// <summary>Idle-rail helper when only rollup health is known (no snapshot list).</summary>
    public static string FormatLabelFromHealth(MonitorHealth health, bool webReady = false)
    {
        _ = webReady;
        return health switch
        {
            MonitorHealth.Red => "Needs fix",
            MonitorHealth.Green => "Healthy",
            MonitorHealth.Amber => AttentionLabel,
            _ => "Monitoring"
        };
    }

    private static StatusPanelOverallSummary SummarizeUnknown(IReadOnlyList<ProjectHealthSnapshot> active)
    {
        // A single held-back project (its own card) says so; the rollup keeps "Monitoring".
        if (active.Count == 1 && !string.IsNullOrWhiteSpace(active[0].NotStartedReason))
        {
            return new StatusPanelOverallSummary(NotStartedLabel, ToolTip: active[0].NotStartedReason);
        }

        return new StatusPanelOverallSummary("Monitoring");
    }

    private static StatusPanelOverallSummary SummarizeAmber(IReadOnlyList<ProjectHealthSnapshot> active)
    {
        var causes = active
            .Where(s => s.Health == MonitorHealth.Amber || IsAuthOrNetworkDegraded(s))
            .SelectMany(s => DescribeAttentionCauses(s).Select(c => (Snapshot: s, Cause: c)))
            .ToList();

        // Auth/network degradation wins over activity so a stuck sign-in is not hidden by a build.
        if (!causes.Any(c => c.Cause.Kind <= AttentionCauseKind.CiUnreachable)
            && active.Any(IsBuildingOrAzureActivity))
        {
            return new StatusPanelOverallSummary("Building");
        }

        if (causes.Count == 0)
        {
            return new StatusPanelOverallSummary(AttentionLabel);
        }

        var primaryKind = causes.Min(c => c.Cause.Kind);
        var reason = primaryKind == AttentionCauseKind.Warnings
            ? FormatWarnings(causes.Where(c => c.Cause.Kind == AttentionCauseKind.Warnings).Sum(c => c.Cause.Count))
            : causes.First(c => c.Cause.Kind == primaryKind).Cause.Text;

        var toolTip = string.Join(
            Environment.NewLine,
            causes.Select(c => active.Count == 1
                ? Capitalize(c.Cause.Text)
                : $"{c.Snapshot.DisplayName}: {c.Cause.Text}"));

        return new StatusPanelOverallSummary(AttentionLabel, reason, toolTip);
    }

    private enum AttentionCauseKind
    {
        SignInRequired = 0,
        CiUnreachable = 1,
        Warnings = 2,
        CiPartial = 3
    }

    private sealed record AttentionCause(AttentionCauseKind Kind, string Text, int Count = 0);

    private static IEnumerable<AttentionCause> DescribeAttentionCauses(ProjectHealthSnapshot snapshot)
    {
        switch (snapshot.Azure?.Availability)
        {
            case AzureMonitoringAvailability.AuthRequired:
                yield return new AttentionCause(AttentionCauseKind.SignInRequired, SignInRequiredReason);
                break;
            case AzureMonitoringAvailability.Unavailable:
                yield return new AttentionCause(AttentionCauseKind.CiUnreachable, CiUnreachableReason);
                break;
        }

        if (snapshot.WarningCount > 0 && snapshot.ErrorCount == 0)
        {
            yield return new AttentionCause(
                AttentionCauseKind.Warnings,
                FormatWarnings(snapshot.WarningCount),
                snapshot.WarningCount);
        }

        if (snapshot.Azure?.CiState == AzureCiMonitoringState.Warning)
        {
            yield return new AttentionCause(AttentionCauseKind.CiPartial, CiPartialReason);
        }
    }

    private static string FormatWarnings(int count) =>
        count == 1 ? "1 warning" : $"{count} warnings";

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    public static bool IsAuthOrNetworkDegraded(ProjectHealthSnapshot snapshot) =>
        snapshot.Azure?.Availability is AzureMonitoringAvailability.AuthRequired
            or AzureMonitoringAvailability.Unavailable;

    public static bool IsBuildingOrAzureActivity(ProjectHealthSnapshot snapshot)
    {
        if (snapshot.IsRestarting
            || snapshot.State is ProjectLifecycleState.Building or ProjectLifecycleState.Testing)
        {
            return true;
        }

        var azure = snapshot.Azure;
        if (azure is null)
        {
            return false;
        }

        if (azure.CiState == AzureCiMonitoringState.Activity)
        {
            return true;
        }

        return azure.PrimaryRun is not null && AzureRunSelector.IsActive(azure.PrimaryRun.State);
    }
}
