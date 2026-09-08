using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Builds current-state <see cref="ProjectFailureDetails"/> for Local build/test (#111a).
/// History may enrich matched ids only — never decides that a failure is current.
/// </summary>
public static class ProjectFailureDetailsBuilder
{
    public static ProjectFailureDetails? Build(
        ProjectHealthSnapshot snapshot,
        IReadOnlyList<OperationalEvent>? recentHistory = null)
    {
        var reasons = new List<FailureReason>(2);

        if (TryBuildLocalBuildReason(snapshot, recentHistory, out var buildReason))
        {
            reasons.Add(buildReason);
        }

        if (TryBuildLocalTestsReason(snapshot, recentHistory, out var testsReason))
        {
            reasons.Add(testsReason);
        }

        return reasons.Count == 0 ? null : new ProjectFailureDetails(reasons);
    }

    /// <summary>
    /// Current Local build failure is authoritative when the last build exit code is still failed
    /// and Local is not mid-build / waiting / mid-test.
    /// </summary>
    public static bool IsCurrentLocalBuildFailure(ProjectHealthSnapshot snapshot) =>
        HealthIssueCountsFormatter.HasFailedCurrentBuild(snapshot.LastBuildExitCode)
        && snapshot.State is not (ProjectLifecycleState.Building
            or ProjectLifecycleState.WaitingForEdits
            or ProjectLifecycleState.Testing);

    /// <summary>Current Local test failure is the completed <see cref="ProjectLifecycleState.TestFailed"/> state.</summary>
    public static bool IsCurrentLocalTestFailure(ProjectHealthSnapshot snapshot) =>
        snapshot.State == ProjectLifecycleState.TestFailed;

    private static bool TryBuildLocalBuildReason(
        ProjectHealthSnapshot snapshot,
        IReadOnlyList<OperationalEvent>? recentHistory,
        out FailureReason reason)
    {
        reason = null!;
        if (!IsCurrentLocalBuildFailure(snapshot))
        {
            return false;
        }

        var matched = FindMatchingFailedBuild(snapshot, recentHistory);
        var preview = FirstNonEmpty(
            snapshot.LastErrorPreview,
            matched?.Detail?.ErrorPreview);
        var shortReason = string.IsNullOrWhiteSpace(preview)
            ? "Open build log for details"
            : BuildErrorCompactFormatter.Format(preview);
        if (string.IsNullOrWhiteSpace(shortReason))
        {
            shortReason = "Open build log for details";
        }

        var exitCode = matched?.Detail?.ExitCode
                       ?? (snapshot.LastBuildExitCode >= 0 ? snapshot.LastBuildExitCode : snapshot.LastExitCode);
        var buildNumber = snapshot.LastFailedLocalBuildNumber ?? matched?.LocalBuildNumber;
        var triggerId = FirstNonEmpty(snapshot.LastFailedBuildTriggerId, matched?.BuildTriggerId);
        var operationId = FirstNonEmpty(snapshot.LastFailedBuildOperationId, matched?.OperationId);
        var observedAt = matched?.OccurredAtUtc ?? snapshot.LastBuildFinishedAtUtc ?? snapshot.LastChangedUtc;

        reason = new FailureReason(
            Source: FailureSourceKind.LocalBuild,
            Title: "Build failed",
            ShortReason: shortReason,
            Severity: FailureSeverity.Error,
            Actions: BuildLocalBuildActions(),
            Detail: null,
            ObservedAtUtc: observedAt,
            ExitCode: exitCode,
            LocalBuildNumber: buildNumber,
            BuildTriggerId: triggerId,
            OperationId: operationId,
            LogKind: BuildLogKind.Build);
        return true;
    }

    private static bool TryBuildLocalTestsReason(
        ProjectHealthSnapshot snapshot,
        IReadOnlyList<OperationalEvent>? recentHistory,
        out FailureReason reason)
    {
        reason = null!;
        if (!IsCurrentLocalTestFailure(snapshot))
        {
            return false;
        }

        var current = snapshot.LastTestFailure;
        var matched = FindMatchingFailedTests(snapshot, recentHistory);

        // Same OperationId ⇒ history detail is the same completed run; prefer it to fill gaps
        // when the live snapshot only has a partial count/names.
        var failedCount = matched?.Detail?.TestFailedCount
                          ?? current?.FailedCount
                          ?? (snapshot.ErrorCount > 0 ? snapshot.ErrorCount : (int?)null);
        var names = TakeNames(
            current?.FailingTestNames,
            matched?.Detail?.FailingTestNames,
            ProjectFailureDetails.MaxFailingTestNamesOnCard);
        var detail = FirstNonEmpty(
            current?.FirstFailureMessage,
            ExtractAssertionFromPreview(matched?.Detail?.ErrorPreview));

        string title;
        string shortReason;
        if (failedCount is > 0)
        {
            title = failedCount == 1 ? "1 test failed" : $"{failedCount} tests failed";
            shortReason = names.Count > 0
                ? string.Join(" · ", names)
                : "Open test log for details";
        }
        else
        {
            title = "Tests failed";
            shortReason = names.Count > 0
                ? string.Join(" · ", names)
                : FirstNonEmpty(
                      BuildErrorCompactFormatter.Format(snapshot.LastErrorPreview),
                      "Open test log for details")
                  ?? "Open test log for details";
        }

        if (string.IsNullOrWhiteSpace(shortReason))
        {
            shortReason = "Open test log for details";
        }

        reason = new FailureReason(
            Source: FailureSourceKind.LocalTests,
            Title: title,
            ShortReason: shortReason,
            Severity: FailureSeverity.Error,
            Actions: BuildLocalTestActions(),
            Detail: string.IsNullOrWhiteSpace(detail) ? null : TrimDetail(detail),
            ObservedAtUtc: matched?.OccurredAtUtc ?? snapshot.LastChangedUtc,
            ExitCode: matched?.Detail?.ExitCode
                      ?? (current?.FailedCount is > 0 ? 1 : snapshot.LastExitCode),
            OperationId: FirstNonEmpty(current?.OperationId, matched?.OperationId),
            LogKind: BuildLogKind.Test);
        return true;
    }

    private static IReadOnlyList<FailureAction> BuildLocalBuildActions() =>
    [
        // Rebuild / Restart live on the card toolbar to avoid duplicate recovery buttons (#111a).
        new(FailureActionKind.OpenBuildLog, "Open build log"),
        new(FailureActionKind.CopyErrors, "Copy errors")
    ];

    private static IReadOnlyList<FailureAction> BuildLocalTestActions() =>
    [
        new(FailureActionKind.OpenTestLog, "Open test log")
        // Run tests stays on the card toolbar (Tests) to avoid duplicate controls.
    ];

    private static OperationalEvent? FindMatchingFailedBuild(
        ProjectHealthSnapshot snapshot,
        IReadOnlyList<OperationalEvent>? recentHistory)
    {
        if (recentHistory is null || recentHistory.Count == 0)
        {
            return null;
        }

        OperationalEvent? byNumber = null;
        OperationalEvent? byTrigger = null;
        OperationalEvent? byOperation = null;

        foreach (var entry in recentHistory)
        {
            if (entry.Kind != OperationalEventKind.Build
                || entry.Outcome != OperationalEventOutcome.Failed
                || !string.Equals(entry.ProjectId, snapshot.ProjectId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (snapshot.LastFailedLocalBuildNumber is int n
                && entry.LocalBuildNumber == n
                && byNumber is null)
            {
                byNumber = entry;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.LastFailedBuildTriggerId)
                && string.Equals(entry.BuildTriggerId, snapshot.LastFailedBuildTriggerId, StringComparison.Ordinal)
                && byTrigger is null)
            {
                byTrigger = entry;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.LastFailedBuildOperationId)
                && string.Equals(entry.OperationId, snapshot.LastFailedBuildOperationId, StringComparison.Ordinal)
                && byOperation is null)
            {
                byOperation = entry;
            }
        }

        return byNumber ?? byTrigger ?? byOperation;
    }

    private static OperationalEvent? FindMatchingFailedTests(
        ProjectHealthSnapshot snapshot,
        IReadOnlyList<OperationalEvent>? recentHistory)
    {
        if (recentHistory is null || recentHistory.Count == 0)
        {
            return null;
        }

        var operationId = snapshot.LastTestFailure?.OperationId;
        if (string.IsNullOrWhiteSpace(operationId))
        {
            // Snapshot should carry structured test failure; without OperationId do not guess from history.
            return null;
        }

        foreach (var entry in recentHistory)
        {
            if (entry.Kind != OperationalEventKind.Tests
                || entry.Outcome != OperationalEventOutcome.Failed
                || !string.Equals(entry.ProjectId, snapshot.ProjectId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(entry.OperationId, operationId, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> TakeNames(
        IReadOnlyList<string>? primary,
        IReadOnlyList<string>? secondary,
        int max)
    {
        var source = primary is { Count: > 0 } ? primary : secondary;
        if (source is null || source.Count == 0 || max < 1)
        {
            return [];
        }

        if (source.Count <= max)
        {
            return source;
        }

        return source.Take(max).ToArray();
    }

    private static string? ExtractAssertionFromPreview(string? preview)
    {
        if (string.IsNullOrWhiteSpace(preview))
        {
            return null;
        }

        var sep = preview.IndexOf(" — ", StringComparison.Ordinal);
        if (sep >= 0 && sep + 3 < preview.Length)
        {
            return preview[(sep + 3)..].Trim();
        }

        return preview.Trim();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string TrimDetail(string detail)
    {
        var trimmed = detail.Trim();
        return trimmed.Length <= 180 ? trimmed : trimmed[..179].TrimEnd() + "…";
    }
}
