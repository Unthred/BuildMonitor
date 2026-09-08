using System.Globalization;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Builds current-state <see cref="ProjectFailureDetails"/> for Local + Azure (#111a / #111b).
/// History may enrich matched ids only — never decides that a failure is current.
/// Azure reasons use the current <see cref="ProjectAzureHealthFacet"/> only (no timeline fetch).
/// </summary>
public static class ProjectFailureDetailsBuilder
{
    public static ProjectFailureDetails? Build(
        ProjectHealthSnapshot snapshot,
        IReadOnlyList<OperationalEvent>? recentHistory = null)
    {
        var reasons = new List<FailureReason>(4);

        if (TryBuildLocalBuildReason(snapshot, recentHistory, out var buildReason))
        {
            reasons.Add(buildReason);
        }

        if (TryBuildLocalTestsReason(snapshot, recentHistory, out var testsReason))
        {
            reasons.Add(testsReason);
        }

        var hasLocal = reasons.Count > 0;

        if (TryBuildAzureCiReason(snapshot, recentHistory, compact: hasLocal, out var azureCiReason))
        {
            reasons.Add(azureCiReason);
        }
        else if (TryBuildAzureAttentionOnlyReason(snapshot, out var attentionReason))
        {
            // Primary is healthy/active but another pipeline drives Attention — compact warning only.
            reasons.Add(attentionReason);
        }

        if (TryBuildAzureAvailabilityReason(snapshot, out var availabilityReason))
        {
            reasons.Add(availabilityReason);
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

    /// <summary>
    /// Azure CI failure/warning when availability is Available and PrimaryRun is a completed
    /// Failed / PartiallySucceeded run matching CiState.
    /// </summary>
    public static bool IsCurrentAzureCiFailure(ProjectHealthSnapshot snapshot)
    {
        var azure = snapshot.Azure;
        if (azure is null || azure.Availability != AzureMonitoringAvailability.Available)
        {
            return false;
        }

        var primary = azure.PrimaryRun;
        if (primary is null || primary.State != PipelineRunState.Completed)
        {
            return false;
        }

        return primary.Result switch
        {
            PipelineRunResult.Failed when azure.CiState == AzureCiMonitoringState.Failed => true,
            PipelineRunResult.PartiallySucceeded when azure.CiState is AzureCiMonitoringState.Warning
                or AzureCiMonitoringState.Failed => true,
            _ => false
        };
    }

    public static bool IsCurrentAzureAvailabilityIssue(ProjectHealthSnapshot snapshot) =>
        snapshot.Azure?.Availability is AzureMonitoringAvailability.AuthRequired
            or AzureMonitoringAvailability.Unavailable;

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

    private static bool TryBuildAzureCiReason(
        ProjectHealthSnapshot snapshot,
        IReadOnlyList<OperationalEvent>? recentHistory,
        bool compact,
        out FailureReason reason)
    {
        reason = null!;
        if (!IsCurrentAzureCiFailure(snapshot))
        {
            return false;
        }

        var azure = snapshot.Azure!;
        var primary = azure.PrimaryRun!;
        var matched = FindMatchingAzureHistory(snapshot.ProjectId, primary.RunId, recentHistory);
        var isPartial = primary.Result == PipelineRunResult.PartiallySucceeded;
        var severity = isPartial ? FailureSeverity.Warning : FailureSeverity.Error;
        var runLabel = FormatAzureRunLabel(primary);
        var branch = FirstNonEmpty(primary.Branch, matched?.Branch, azure.FocusBranch) ?? string.Empty;
        var pipeline = FirstNonEmpty(primary.PipelineDisplayName);

        string title;
        string shortReason;
        if (compact)
        {
            title = isPartial
                ? $"Azure · {runLabel} partially succeeded"
                : $"Azure · {runLabel} failed";
            shortReason = FirstNonEmpty(branch, pipeline) ?? DescribeAzureResult(primary);
        }
        else
        {
            title = isPartial ? "Azure build partially succeeded" : "Azure build failed";
            shortReason = FormatAzurePrimaryShortReason(primary, pipeline, runLabel, branch, matched);
        }

        var detailParts = new List<string>(2);
        var attention = FormatAttentionNeeds(azure.AttentionRuns);
        if (!string.IsNullOrWhiteSpace(attention))
        {
            detailParts.Add(attention);
        }

        // Prefer AzureStage / transition only when it adds context beyond State/Result already known.
        if (!string.IsNullOrWhiteSpace(matched?.Detail?.AzureStage)
            && !string.Equals(matched!.Detail!.AzureStage, $"{primary.State}/{primary.Result}", StringComparison.Ordinal))
        {
            detailParts.Add(matched.Detail.AzureStage!);
        }
        else if (!string.IsNullOrWhiteSpace(matched?.PreviousValue)
                 && !string.IsNullOrWhiteSpace(matched.NewValue))
        {
            detailParts.Add($"{matched.PreviousValue} → {matched.NewValue}");
        }

        var runUrl = ResolveAzureRunUrl(primary, azure.NavigationContext);
        var actions = BuildAzureCiActions(snapshot.ProjectId, primary, azure.NavigationContext, runUrl);

        reason = new FailureReason(
            Source: FailureSourceKind.AzureCi,
            Title: title,
            ShortReason: shortReason,
            Severity: severity,
            Actions: actions,
            Detail: detailParts.Count == 0 ? null : string.Join(" · ", detailParts),
            ObservedAtUtc: matched?.OccurredAtUtc
                           ?? primary.FinishedAtUtc
                           ?? primary.StartedAtUtc
                           ?? azure.PolledAtUtc,
            AzureRunId: primary.RunId,
            Url: runUrl);
        return true;
    }

    private static bool TryBuildAzureAttentionOnlyReason(
        ProjectHealthSnapshot snapshot,
        out FailureReason reason)
    {
        reason = null!;
        var azure = snapshot.Azure;
        if (azure is null || azure.Availability != AzureMonitoringAvailability.Available)
        {
            return false;
        }

        // Only when Primary is not already represented as the CI failure card.
        if (IsCurrentAzureCiFailure(snapshot))
        {
            return false;
        }

        if (azure.CiState is not (AzureCiMonitoringState.Failed or AzureCiMonitoringState.Warning))
        {
            return false;
        }

        var attentionLine = FormatAttentionNeeds(azure.AttentionRuns);
        if (string.IsNullOrWhiteSpace(attentionLine))
        {
            return false;
        }

        reason = new FailureReason(
            Source: FailureSourceKind.AzureCi,
            Title: "Azure pipelines need attention",
            ShortReason: attentionLine,
            Severity: FailureSeverity.Warning,
            Actions: [],
            ObservedAtUtc: azure.PolledAtUtc);
        return true;
    }

    private static bool TryBuildAzureAvailabilityReason(
        ProjectHealthSnapshot snapshot,
        out FailureReason reason)
    {
        reason = null!;
        var azure = snapshot.Azure;
        if (azure is null)
        {
            return false;
        }

        if (azure.Availability == AzureMonitoringAvailability.AuthRequired)
        {
            reason = new FailureReason(
                Source: FailureSourceKind.AzureAvailability,
                Title: "Azure sign-in required",
                ShortReason: FirstNonEmpty(azure.StatusMessage, "Sign in to resume Azure monitoring")
                             ?? "Sign in to resume Azure monitoring",
                Severity: FailureSeverity.Warning,
                Actions: [],
                ObservedAtUtc: azure.PolledAtUtc);
            return true;
        }

        if (azure.Availability == AzureMonitoringAvailability.Unavailable)
        {
            reason = new FailureReason(
                Source: FailureSourceKind.AzureAvailability,
                Title: "Azure monitoring unavailable",
                ShortReason: FirstNonEmpty(azure.StatusMessage, "Azure DevOps could not be reached")
                             ?? "Azure DevOps could not be reached",
                Severity: FailureSeverity.Warning,
                Actions: [],
                ObservedAtUtc: azure.PolledAtUtc);
            return true;
        }

        return false;
    }

    private static string FormatAzurePrimaryShortReason(
        AzurePipelineRunInfo primary,
        string? pipeline,
        string runLabel,
        string branch,
        OperationalEvent? matched)
    {
        var enrichedBuild = FirstNonEmpty(
            FormatDisplayBuildNumber(primary.BuildNumber),
            matched?.AzureBuildNumber is string bn && !string.IsNullOrWhiteSpace(bn)
                ? bn.Trim()
                : null);
        var usePipeline = !string.IsNullOrWhiteSpace(pipeline)
                          && pipeline!.Length <= 28
                          && !string.Equals(pipeline, branch, StringComparison.OrdinalIgnoreCase);

        if (usePipeline)
        {
            // MasterCI #553 · master  OR  MasterCI #553 failed style when branch empty
            var head = $"{pipeline} {runLabel}";
            if (!string.IsNullOrWhiteSpace(branch))
            {
                return $"{head} · {branch}";
            }

            if (!string.IsNullOrWhiteSpace(enrichedBuild)
                && !runLabel.Contains(enrichedBuild, StringComparison.OrdinalIgnoreCase))
            {
                return $"{head} · {enrichedBuild}";
            }

            return head;
        }

        if (!string.IsNullOrWhiteSpace(branch))
        {
            return $"{runLabel} · {branch}";
        }

        return FirstNonEmpty(enrichedBuild, DescribeAzureResult(primary)) ?? runLabel;
    }

    private static string FormatAzureRunLabel(AzurePipelineRunInfo run) =>
        string.Create(CultureInfo.InvariantCulture, $"#{run.RunId}");

    private static string? FormatDisplayBuildNumber(string? buildNumber) =>
        string.IsNullOrWhiteSpace(buildNumber) ? null : buildNumber.Trim();

    private static string DescribeAzureResult(AzurePipelineRunInfo run) =>
        run.Result switch
        {
            PipelineRunResult.Failed => "Failed",
            PipelineRunResult.PartiallySucceeded => "Partially succeeded",
            PipelineRunResult.Canceled => "Cancelled",
            PipelineRunResult.Succeeded => "Succeeded",
            _ => run.State.ToString()
        };

    private static string? FormatAttentionNeeds(IReadOnlyList<AzurePipelineRunInfo> attention)
    {
        if (attention.Count == 0)
        {
            return null;
        }

        var needing = attention.Count(r =>
            r.State == PipelineRunState.Completed
            && r.Result is PipelineRunResult.Failed or PipelineRunResult.PartiallySucceeded);
        if (needing <= 0)
        {
            return null;
        }

        return needing == 1
            ? "1 other pipeline needs attention"
            : string.Create(CultureInfo.InvariantCulture, $"{needing} other pipelines need attention");
    }

    private static string? ResolveAzureRunUrl(
        AzurePipelineRunInfo primary,
        AzureBuildNavigationContext? navigationContext)
    {
        if (!string.IsNullOrWhiteSpace(primary.RunUrl)
            && Uri.TryCreate(primary.RunUrl, UriKind.Absolute, out _))
        {
            return primary.RunUrl.Trim();
        }

        if (navigationContext is null)
        {
            return null;
        }

        return AzureDevOpsDeepLinkBuilder.BuildRunResultsUrl(
            navigationContext.OrganizationUrl,
            navigationContext.AdoProjectIdOrName,
            primary.RunId);
    }

    private static IReadOnlyList<FailureAction> BuildAzureCiActions(
        string projectId,
        AzurePipelineRunInfo primary,
        AzureBuildNavigationContext? navigationContext,
        string? runUrl)
    {
        var actions = new List<FailureAction>(2);
        if (!string.IsNullOrWhiteSpace(runUrl))
        {
            actions.Add(new FailureAction(FailureActionKind.OpenAzureRun, "Open Azure run", Url: runUrl));
        }

        if (navigationContext is not null)
        {
            var nav = AzureBuildSourceNavigationBuilder.Build(primary, navigationContext);
            if (nav.FailureRequest is not null)
            {
                // FailureRequest is only present when NeedsFailureResolution — timeline stays click-lazy.
                actions.Add(new FailureAction(
                    FailureActionKind.OpenAzureFailureLogs,
                    "Open failure logs",
                    AzureFailureRequest: nav.FailureRequest with { ProjectId = projectId }));
            }
        }

        return actions;
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

    private static OperationalEvent? FindMatchingAzureHistory(
        string projectId,
        long azureRunId,
        IReadOnlyList<OperationalEvent>? recentHistory)
    {
        if (recentHistory is null || recentHistory.Count == 0)
        {
            return null;
        }

        foreach (var entry in recentHistory)
        {
            if (entry.Kind != OperationalEventKind.AzureRun
                || entry.AzureRunId != azureRunId
                || !string.Equals(entry.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return entry;
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
