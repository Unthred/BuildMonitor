using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class ProjectFailureDetailsAzureBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly AzureBuildNavigationContext NavContext = new(
        "p1",
        "conn-1",
        "https://dev.azure.com/org",
        "My Project",
        "MyRepo",
        "repo-id-1");

    [Fact]
    public void Current_azure_failed_run_maps_azure_ci_reason()
    {
        var run = Run(553, "MasterCI", PipelineRunResult.Failed, "master");
        var details = ProjectFailureDetailsBuilder.Build(HealthyLocal(AzureFacet(run, AzureCiMonitoringState.Failed)));

        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Equal(FailureSourceKind.AzureCi, reason.Source);
        Assert.Equal(FailureSeverity.Error, reason.Severity);
        Assert.Equal("Azure build failed", reason.Title);
        Assert.Contains("#553", reason.ShortReason, StringComparison.Ordinal);
        Assert.Contains("master", reason.ShortReason, StringComparison.Ordinal);
        Assert.Equal(553, reason.AzureRunId);
        Assert.Contains(reason.Actions, a => a.Kind == FailureActionKind.OpenAzureRun);
        Assert.Contains(reason.Actions, a => a.Kind == FailureActionKind.OpenAzureFailureLogs);
        Assert.Equal(run.RunUrl, reason.Actions.First(a => a.Kind == FailureActionKind.OpenAzureRun).Url);
        Assert.Equal(553, reason.Actions.First(a => a.Kind == FailureActionKind.OpenAzureFailureLogs).AzureFailureRequest!.RunId);
    }

    [Fact]
    public void Successful_azure_run_has_no_azure_failure_reason()
    {
        var run = Run(100, "CI", PipelineRunResult.Succeeded, "master");
        Assert.Null(ProjectFailureDetailsBuilder.Build(
            HealthyLocal(AzureFacet(run, AzureCiMonitoringState.Healthy))));
    }

    [Fact]
    public void Partially_succeeded_is_warning_not_failed_label()
    {
        var run = Run(40, "CI", PipelineRunResult.PartiallySucceeded, "release");
        var details = ProjectFailureDetailsBuilder.Build(
            HealthyLocal(AzureFacet(run, AzureCiMonitoringState.Warning)));

        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Equal(FailureSeverity.Warning, reason.Severity);
        Assert.Equal("Azure build partially succeeded", reason.Title);
        Assert.DoesNotContain("failed", reason.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(reason.Actions, a => a.Kind == FailureActionKind.OpenAzureFailureLogs);
    }

    [Fact]
    public void Auth_required_maps_availability_warning_not_ci_failure()
    {
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.AuthRequired,
            AzureCiMonitoringState.NotMonitored,
            "master",
            null,
            [],
            Now,
            "PAT expired");
        var details = ProjectFailureDetailsBuilder.Build(HealthyLocal(facet));

        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Equal(FailureSourceKind.AzureAvailability, reason.Source);
        Assert.Equal(FailureSeverity.Warning, reason.Severity);
        Assert.Equal("Azure sign-in required", reason.Title);
        Assert.Equal("PAT expired", reason.ShortReason);
        Assert.Empty(reason.Actions);
        Assert.Null(reason.AzureRunId);
    }

    [Fact]
    public void Unavailable_maps_availability_warning_not_ci_failure()
    {
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Unavailable,
            AzureCiMonitoringState.NotMonitored,
            null,
            null,
            [],
            Now,
            "DNS failure");
        var details = ProjectFailureDetailsBuilder.Build(HealthyLocal(facet));

        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Equal(FailureSourceKind.AzureAvailability, reason.Source);
        Assert.Equal("Azure monitoring unavailable", reason.Title);
        Assert.DoesNotContain(reason.Actions, a => a.Kind == FailureActionKind.OpenAzureRun);
    }

    [Fact]
    public void Auth_does_not_masquerade_as_ci_even_when_stale_run_present()
    {
        var stale = Run(99, "CI", PipelineRunResult.Failed, "master");
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.AuthRequired,
            AzureCiMonitoringState.NotMonitored,
            "master",
            stale,
            [],
            Now,
            "sign in",
            NavigationContext: NavContext);
        var details = ProjectFailureDetailsBuilder.Build(HealthyLocal(facet));

        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Equal(FailureSourceKind.AzureAvailability, reason.Source);
        Assert.Empty(reason.Actions);
        Assert.DoesNotContain(reason.Actions, a => a.Kind == FailureActionKind.OpenAzureRun);
        Assert.DoesNotContain(reason.Actions, a => a.Kind == FailureActionKind.OpenAzureFailureLogs);
    }

    [Fact]
    public void Unavailable_with_stale_primary_run_leaks_no_azure_actions()
    {
        var stale = Run(88, "CI", PipelineRunResult.Failed, "master");
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Unavailable,
            AzureCiMonitoringState.NotMonitored,
            "master",
            stale,
            [],
            Now,
            "timeout",
            NavigationContext: NavContext);
        var details = ProjectFailureDetailsBuilder.Build(HealthyLocal(facet));

        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Equal(FailureSourceKind.AzureAvailability, reason.Source);
        Assert.Equal(FailureSeverity.Warning, reason.Severity);
        Assert.Empty(reason.Actions);
    }

    [Fact]
    public void Failed_run_with_url_only_hides_failure_logs()
    {
        // URL present, no NavigationContext ⇒ no FailureRequest identity.
        var run = Run(10, "CI", PipelineRunResult.Failed, "master");
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Failed,
            "master",
            run,
            [],
            Now,
            NavigationContext: null);
        var details = ProjectFailureDetailsBuilder.Build(HealthyLocal(facet));
        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Contains(reason.Actions, a => a.Kind == FailureActionKind.OpenAzureRun);
        Assert.Equal(run.RunUrl, reason.Actions.Single(a => a.Kind == FailureActionKind.OpenAzureRun).Url);
        Assert.DoesNotContain(reason.Actions, a => a.Kind == FailureActionKind.OpenAzureFailureLogs);
    }

    [Fact]
    public void Failed_run_without_run_url_uses_navigation_deep_link_and_failure_logs()
    {
        // Architecture: FailureRequest requires NavigationContext; that same context
        // yields a valid run-results deep-link — never a broken fabricated URL.
        var run = Run(42, "CI", PipelineRunResult.Failed, "master") with { RunUrl = null };
        var details = ProjectFailureDetailsBuilder.Build(
            HealthyLocal(AzureFacet(run, AzureCiMonitoringState.Failed)));
        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);

        var openRun = Assert.Single(reason.Actions, a => a.Kind == FailureActionKind.OpenAzureRun);
        Assert.False(string.IsNullOrWhiteSpace(openRun.Url));
        Assert.Contains("buildId=42", openRun.Url!, StringComparison.Ordinal);
        Assert.StartsWith("https://", openRun.Url!, StringComparison.OrdinalIgnoreCase);

        var failureLogs = Assert.Single(reason.Actions, a => a.Kind == FailureActionKind.OpenAzureFailureLogs);
        Assert.NotNull(failureLogs.AzureFailureRequest);
        Assert.Equal(42, failureLogs.AzureFailureRequest!.RunId);
    }

    [Fact]
    public void Local_and_azure_concurrent_preserve_order()
    {
        var run = Run(553, "MasterCI", PipelineRunResult.Failed, "master");
        var snapshot = LocalFailed() with { Azure = AzureFacet(run, AzureCiMonitoringState.Failed) };

        var details = ProjectFailureDetailsBuilder.Build(snapshot);
        Assert.NotNull(details);
        Assert.Equal(2, details.Reasons.Count);
        Assert.Equal(FailureSourceKind.LocalBuild, details.Primary.Source);
        Assert.Equal(FailureSourceKind.AzureCi, details.Reasons[1].Source);
        Assert.StartsWith("Azure · #553 failed", details.Reasons[1].Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Stale_failed_azure_history_ignored_when_current_run_healthy()
    {
        var healthy = Run(200, "CI", PipelineRunResult.Succeeded, "master");
        var history = new[]
        {
            AzureEvent(199, OperationalEventOutcome.Failed, "Azure #199 failed")
        };

        Assert.Null(ProjectFailureDetailsBuilder.Build(
            HealthyLocal(AzureFacet(healthy, AzureCiMonitoringState.Healthy)),
            history));
    }

    [Fact]
    public void Matching_azure_run_id_history_enriches_detail()
    {
        var run = Run(77, "CI", PipelineRunResult.Failed, "master");
        var history = new[]
        {
            AzureEvent(
                77,
                OperationalEventOutcome.Failed,
                "Azure #77 failed",
                previousValue: "InProgress/Unknown",
                newValue: "Completed/Failed",
                detail: new OperationalEventDetail(AzureStage: "Deploy"))
        };

        var details = ProjectFailureDetailsBuilder.Build(
            HealthyLocal(AzureFacet(run, AzureCiMonitoringState.Failed)),
            history);
        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Equal(77, reason.AzureRunId);
        Assert.Equal("Deploy", reason.Detail);
    }

    [Fact]
    public void Builder_does_not_require_timeline_client_for_mapping()
    {
        // Mapping only reads facet + optional history; FailureRequest is identity for lazy click.
        var run = Run(5, "CI", PipelineRunResult.Failed, "dev");
        var details = ProjectFailureDetailsBuilder.Build(
            HealthyLocal(AzureFacet(run, AzureCiMonitoringState.Failed)));
        Assert.NotNull(details);
        var failureAction = Assert.Single(
            details.Primary.Actions,
            a => a.Kind == FailureActionKind.OpenAzureFailureLogs);
        Assert.NotNull(failureAction.AzureFailureRequest);
        Assert.Equal(5, failureAction.AzureFailureRequest!.RunId);
    }

    [Fact]
    public void Attention_runs_appear_as_detail_not_second_primary()
    {
        var primary = Run(1, "CI", PipelineRunResult.Failed, "master");
        var other = Run(2, "Security", PipelineRunResult.Failed, "master");
        var facet = AzureFacet(primary, AzureCiMonitoringState.Failed) with
        {
            AttentionRuns = [other]
        };

        var details = ProjectFailureDetailsBuilder.Build(HealthyLocal(facet));
        Assert.NotNull(details);
        Assert.Single(details.Reasons);
        Assert.Equal("1 other pipeline needs attention", details.Primary.Detail);
    }

    [Fact]
    public void Attention_only_when_primary_healthy_but_ci_failed()
    {
        var primary = Run(10, "CI", PipelineRunResult.Succeeded, "master");
        var other = Run(11, "Nightly", PipelineRunResult.Failed, "master");
        var facet = new ProjectAzureHealthFacet(
            AzureMonitoringAvailability.Available,
            AzureCiMonitoringState.Failed,
            "master",
            primary,
            [other],
            Now,
            NavigationContext: NavContext);

        var details = ProjectFailureDetailsBuilder.Build(HealthyLocal(facet));
        Assert.NotNull(details);
        var reason = Assert.Single(details.Reasons);
        Assert.Equal(FailureSeverity.Warning, reason.Severity);
        Assert.Equal("Azure pipelines need attention", reason.Title);
        Assert.Equal("1 other pipeline needs attention", reason.ShortReason);
        Assert.Empty(reason.Actions);
    }

    [Fact]
    public void Health_composer_still_independent_of_failure_details()
    {
        var local = HealthyLocal(null);
        var run = Run(9, "CI", PipelineRunResult.Failed, "master");
        var composed = ProjectHealthComposer.WithAzure(
            local,
            AzureFacet(run, AzureCiMonitoringState.Failed));

        Assert.Equal(MonitorHealth.Red, composed.Health);
        var details = ProjectFailureDetailsBuilder.Build(composed);
        Assert.NotNull(details);
        Assert.Equal(MonitorHealth.Red, composed.Health);
        Assert.Equal(FailureSourceKind.AzureCi, details.Primary.Source);
    }

    [Fact]
    public void Presentation_keeps_activity_with_azure_failure_details()
    {
        var run = Run(55, "CI", PipelineRunResult.Failed, "master");
        var presentation = StatusPanelPresentationBuilder.Build(
            [HealthyLocal(AzureFacet(run, AzureCiMonitoringState.Failed))],
            null,
            Now);
        var card = Assert.Single(presentation.Cards);
        Assert.NotNull(card.FailureDetails);
        Assert.Equal(FailureSourceKind.AzureCi, card.FailureDetails.Primary.Source);
        Assert.NotNull(card.Activity);
        Assert.False(card.ShowErrorPreview);
    }

    private static ProjectHealthSnapshot HealthyLocal(ProjectAzureHealthFacet? azure) =>
        new(
            ProjectId: "p1",
            DisplayName: "Demo",
            Health: azure is null ? MonitorHealth.Green : MonitorHealth.Unknown,
            HealthLabel: "ok",
            State: ProjectLifecycleState.Watching,
            LastExitCode: 0,
            LastDuration: TimeSpan.FromSeconds(1),
            LastErrorPreview: null,
            ErrorCount: 0,
            WarningCount: 0,
            LastChangedUtc: Now,
            LastBuildFinishedAtUtc: Now.AddMinutes(-1),
            IsActive: true,
            ProgressSteps: [],
            LastBuildExitCode: 0,
            Azure: azure);

    private static ProjectHealthSnapshot LocalFailed() =>
        new(
            ProjectId: "p1",
            DisplayName: "Demo",
            Health: MonitorHealth.Red,
            HealthLabel: "failed",
            State: ProjectLifecycleState.BuildFailed,
            LastExitCode: 1,
            LastDuration: TimeSpan.FromSeconds(1),
            LastErrorPreview: "error CS0001: boom",
            ErrorCount: 1,
            WarningCount: 0,
            LastChangedUtc: Now,
            LastBuildFinishedAtUtc: Now.AddMinutes(-1),
            IsActive: true,
            ProgressSteps: [],
            LastBuildExitCode: 1,
            LastFailedLocalBuildNumber: 3);

    private static ProjectAzureHealthFacet AzureFacet(
        AzurePipelineRunInfo primary,
        AzureCiMonitoringState ci) =>
        new(
            AzureMonitoringAvailability.Available,
            ci,
            primary.Branch,
            primary,
            [],
            Now,
            NavigationContext: NavContext);

    private static AzurePipelineRunInfo Run(
        long runId,
        string pipeline,
        PipelineRunResult result,
        string branch) =>
        new(
            DefinitionId: 1,
            PipelineDisplayName: pipeline,
            RunId: runId,
            BuildNumber: runId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            State: PipelineRunState.Completed,
            Result: result,
            Branch: branch,
            QueuedAtUtc: Now.AddMinutes(-10),
            StartedAtUtc: Now.AddMinutes(-9),
            FinishedAtUtc: Now.AddMinutes(-1),
            RunUrl: $"https://dev.azure.com/org/My%20Project/_build/results?buildId={runId}&view=results",
            SourceBranchRef: $"refs/heads/{branch}");

    private static OperationalEvent AzureEvent(
        long runId,
        OperationalEventOutcome outcome,
        string summary,
        string? previousValue = null,
        string? newValue = null,
        OperationalEventDetail? detail = null) =>
        new(
            SchemaVersion: OperationalHistorySchema.CurrentVersion,
            Id: Guid.NewGuid().ToString("N"),
            ProjectId: "p1",
            OccurredAtUtc: Now.AddMinutes(-5),
            Source: OperationalEventSource.Azure,
            Kind: OperationalEventKind.AzureRun,
            Outcome: outcome,
            Summary: summary,
            Detail: detail,
            AzureRunId: runId,
            AzureBuildNumber: runId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Branch: "master",
            PreviousValue: previousValue,
            NewValue: newValue);
}
