using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Maps tray <see cref="ProjectHealthSnapshot"/> / <see cref="ProjectAzureHealthFacet"/> into control-plane JSON DTOs.
/// Does not call Azure, git, or re-run pipeline selection — primary run is always facet <c>PrimaryRun</c>.
/// </summary>
public static class ControlPlaneProjectStatusMapper
{
    public static ControlPlaneProjectInfo Map(
        string id,
        string displayName,
        string rootFolder,
        string projectFile,
        bool isActiveInSession,
        bool hasLocal,
        bool azureAttached,
        ProjectHealthSnapshot? snapshot,
        ControlPlaneSessionStatus? session,
        DateTimeOffset utcNow)
    {
        MonitorHealth? overall = snapshot?.Health;
        string? overallLabel = snapshot?.HealthLabel;
        if (snapshot is null && !hasLocal && !azureAttached)
        {
            overall = null;
            overallLabel = null;
        }
        else if (snapshot is null)
        {
            overall = MonitorHealth.Unknown;
            overallLabel = ProjectHealthEvaluator.ToLabel(MonitorHealth.Unknown);
        }

        return new ControlPlaneProjectInfo(
            id,
            displayName,
            rootFolder,
            projectFile,
            isActiveInSession,
            overall,
            overallLabel,
            session?.State,
            hasLocal ? MapLocal(snapshot) : null,
            azureAttached ? MapAzure(snapshot?.Azure, utcNow) : null);
    }

    /// <summary>
    /// Maps Azure facet fields for agents. Uses <see cref="ProjectAzureHealthFacet.PrimaryRun"/> only
    /// (same authority as the status panel). Attention runs never replace <see cref="ControlPlaneAzureFacetInfo.RunId"/>.
    /// </summary>
    public static ControlPlaneAzureFacetInfo MapAzure(ProjectAzureHealthFacet? facet, DateTimeOffset utcNow)
    {
        if (facet is null)
        {
            return new ControlPlaneAzureFacetInfo(
                AzureMonitoringAvailability.Available,
                AzureCiMonitoringState.NotMonitored,
                Pipeline: null,
                Status: null,
                Branch: null,
                RunId: null,
                BuildNumber: null,
                PullRequestNumber: null,
                RunUrl: null,
                PolledAtUtc: DateTimeOffset.MinValue,
                AgeSeconds: null,
                StatusMessage: null,
                HasSelectedPipelines: true,
                AttentionSummary: null,
                FocusBranch: null);
        }

        var primary = facet.PrimaryRun;
        string? status = null;
        if (primary is not null)
        {
            status = AzureStatusPresentationBuilder.DescribeRun(primary).StateLabel;
        }

        return new ControlPlaneAzureFacetInfo(
            facet.Availability,
            facet.CiState,
            Pipeline: primary?.PipelineDisplayName,
            Status: status,
            Branch: primary?.Branch,
            RunId: primary is { RunId: > 0 } ? primary.RunId : null,
            BuildNumber: string.IsNullOrWhiteSpace(primary?.BuildNumber) ? null : primary!.BuildNumber.Trim(),
            PullRequestNumber: primary?.PullRequestNumber is > 0 ? primary.PullRequestNumber : null,
            RunUrl: string.IsNullOrWhiteSpace(primary?.RunUrl) ? null : primary!.RunUrl,
            PolledAtUtc: facet.PolledAtUtc,
            AgeSeconds: ComputeAgeSeconds(facet.PolledAtUtc, utcNow),
            StatusMessage: string.IsNullOrWhiteSpace(facet.StatusMessage) ? null : facet.StatusMessage.Trim(),
            HasSelectedPipelines: facet.HasSelectedPipelines,
            AttentionSummary: FormatAttentionSummary(facet.AttentionRuns),
            FocusBranch: facet.FocusBranch);
    }

    public static ControlPlaneLocalFacetInfo MapLocal(ProjectHealthSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return new ControlPlaneLocalFacetInfo(
                MonitorHealth.Unknown,
                Branch: null,
                LastBuildAtUtc: null,
                Errors: 0,
                Warnings: 0,
                LifecycleState: ProjectLifecycleState.Idle,
                LastBuildExitCode: null);
        }

        var localHealth = StatusPanelPresentationBuilder.ResolveLocalBuildHealth(snapshot);
        var branch = snapshot.LocalGit switch
        {
            { HeadStatus: LocalGitHeadStatus.Branch, CurrentBranch: { Length: > 0 } b } => b,
            { HeadStatus: LocalGitHeadStatus.Detached } => "detached",
            _ => null
        };

        return new ControlPlaneLocalFacetInfo(
            localHealth,
            branch,
            snapshot.LastBuildFinishedAtUtc,
            snapshot.ErrorCount,
            snapshot.WarningCount,
            snapshot.State,
            snapshot.LastBuildExitCode >= 0 ? snapshot.LastBuildExitCode : null);
    }

    public static int? ComputeAgeSeconds(DateTimeOffset polledAtUtc, DateTimeOffset utcNow)
    {
        if (polledAtUtc == DateTimeOffset.MinValue)
        {
            return null;
        }

        var age = utcNow - polledAtUtc;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        return (int)Math.Min(int.MaxValue, Math.Floor(age.TotalSeconds));
    }

    /// <summary>Compact attention line for agents; never used as the current run id.</summary>
    public static string? FormatAttentionSummary(IReadOnlyList<AzurePipelineRunInfo> attention)
    {
        if (attention.Count == 0)
        {
            return null;
        }

        var failed = attention.Count(r =>
            r.State == PipelineRunState.Completed && r.Result == PipelineRunResult.Failed);
        if (failed > 0)
        {
            return failed == 1
                ? "1 other pipeline failed"
                : $"{failed} other pipelines failed";
        }

        var warnings = attention.Count(r =>
            r.State == PipelineRunState.Completed && r.Result == PipelineRunResult.PartiallySucceeded);
        if (warnings > 0)
        {
            return warnings == 1
                ? "1 other pipeline warning"
                : $"{warnings} other pipelines warning";
        }

        var active = attention.Count(r => AzureRunSelector.IsActive(r.State));
        if (active > 0)
        {
            return active == 1
                ? "1 other pipeline running"
                : $"{active} other pipelines running";
        }

        return null;
    }
}
