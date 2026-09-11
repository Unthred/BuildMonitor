using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Builds presentation-neutral per-project activity from existing Local/Azure/control-plane snapshots.
/// Does not poll; does not invent progress; does not alter health precedence.
/// </summary>
public static class ProjectActivityBuilder
{
    public static ProjectActivitySet Build(ProjectHealthSnapshot snapshot, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var activities = new List<ProjectActivitySnapshot>(capacity: 4);
        AddAgentOrLocal(snapshot, utcNow, activities);
        AddAzure(snapshot, utcNow, activities);

        var active = activities.Where(a => a.IsActive).ToList();
        var primary = SelectPrimary(active);
        var coexistence = FormatCoexistence(active);

        return new ProjectActivitySet(
            snapshot.ProjectId,
            activities,
            primary,
            primary?.StatusText,
            coexistence);
    }

    public static string FormatRailLabel(ProjectHealthSnapshot snapshot, DateTimeOffset utcNow)
    {
        var set = Build(snapshot, utcNow);
        return set.PrimaryStatusText ?? string.Empty;
    }

    private static void AddAgentOrLocal(
        ProjectHealthSnapshot snapshot,
        DateTimeOffset utcNow,
        List<ProjectActivitySnapshot> activities)
    {
        var controlPlane = snapshot.ControlPlane ?? ProjectControlPlaneSnapshot.Unused;

        if (controlPlane.AgentTestsInProgress)
        {
            if (controlPlane.OperationCancelRequested)
            {
                activities.Add(Create(
                    snapshot.ProjectId,
                    ActivitySourceKind.Agent,
                    ActivityPhaseKind.Cancelling,
                    "Cancelling tests…",
                    utcNow,
                    isActive: true,
                    operationId: controlPlane.ActiveOperationId));
                return;
            }

            var testing = ResolveTestingPresentation(snapshot);
            activities.Add(Create(
                snapshot.ProjectId,
                ActivitySourceKind.Agent,
                ActivityPhaseKind.AgentTests,
                testing.StatusText,
                utcNow,
                isActive: true,
                startedAtUtc: testing.StartedAtUtc,
                progress: testing.Progress,
                operationId: controlPlane.ActiveOperationId));
            return;
        }

        if (controlPlane.AgentRebuildInProgress
            || controlPlane.AgentRebuildPhase != ControlPlaneShipCheckPhase.None)
        {
            if (controlPlane.OperationCancelRequested)
            {
                activities.Add(Create(
                    snapshot.ProjectId,
                    ActivitySourceKind.Agent,
                    ActivityPhaseKind.Cancelling,
                    "Cancelling rebuild…",
                    utcNow,
                    isActive: true,
                    operationId: controlPlane.ActiveOperationId));
                return;
            }

            var (phase, text) = controlPlane.AgentRebuildPhase switch
            {
                ControlPlaneShipCheckPhase.Preparing => (ActivityPhaseKind.AgentRebuild, "Rebuild — preparing"),
                ControlPlaneShipCheckPhase.Building => (ActivityPhaseKind.AgentRebuild, "Rebuild — building"),
                ControlPlaneShipCheckPhase.ResumingWatch => (ActivityPhaseKind.AgentRebuild, "Rebuild — resuming watch"),
                _ => (ActivityPhaseKind.AgentRebuild, "Rebuild — running")
            };
            activities.Add(Create(
                snapshot.ProjectId,
                ActivitySourceKind.Agent,
                phase,
                text,
                utcNow,
                isActive: true,
                operationId: controlPlane.ActiveOperationId));
            return;
        }

        if (controlPlane.ShipCheckPhase != ControlPlaneShipCheckPhase.None
            || controlPlane.ShipCheckInProgress)
        {
            if (controlPlane.OperationCancelRequested)
            {
                activities.Add(Create(
                    snapshot.ProjectId,
                    ActivitySourceKind.Agent,
                    ActivityPhaseKind.Cancelling,
                    "Cancelling ship check…",
                    utcNow,
                    isActive: true,
                    operationId: controlPlane.ActiveOperationId));
                return;
            }

            var (phase, text) = controlPlane.ShipCheckPhase switch
            {
                ControlPlaneShipCheckPhase.Preparing => (ActivityPhaseKind.ShipCheck, "Ship check — preparing"),
                ControlPlaneShipCheckPhase.Building => (ActivityPhaseKind.ShipCheck, "Ship check — building"),
                ControlPlaneShipCheckPhase.Testing => (
                    ActivityPhaseKind.ShipCheck,
                    AppendTestingProgress("Ship check — testing", snapshot)),
                ControlPlaneShipCheckPhase.ResumingWatch => (ActivityPhaseKind.ShipCheck, "Ship check — resuming watch"),
                _ => (ActivityPhaseKind.ShipCheck, "Ship check — running")
            };
            activities.Add(Create(
                snapshot.ProjectId,
                ActivitySourceKind.Agent,
                phase,
                text,
                utcNow,
                isActive: true,
                operationId: controlPlane.ActiveOperationId));
            return;
        }

        if (snapshot.State == ProjectLifecycleState.Testing)
        {
            var testing = ResolveTestingPresentation(snapshot);
            activities.Add(Create(
                snapshot.ProjectId,
                ActivitySourceKind.Local,
                ActivityPhaseKind.Testing,
                testing.StatusText,
                utcNow,
                isActive: true,
                startedAtUtc: testing.StartedAtUtc,
                progress: testing.Progress));
            return;
        }

        if (snapshot.State == ProjectLifecycleState.Building)
        {
            var failed = snapshot.ProgressSteps.FirstOrDefault(s => s.Status == BuildStepStatus.Failed);
            if (failed is not null)
            {
                activities.Add(Create(
                    snapshot.ProjectId,
                    ActivitySourceKind.Local,
                    ActivityPhaseKind.Building,
                    "Build failed",
                    utcNow,
                    isActive: true,
                    detail: failed.Label));
                return;
            }

            var activeStep = snapshot.ProgressSteps.FirstOrDefault(s => s.Status == BuildStepStatus.Active);
            var status = activeStep is null
                ? "Building"
                : FormatActiveBuildStep(activeStep.Label);
            activities.Add(Create(
                snapshot.ProjectId,
                ActivitySourceKind.Local,
                ActivityPhaseKind.Building,
                status,
                utcNow,
                isActive: true,
                detail: activeStep?.Label));
            return;
        }

        if (snapshot.IsRestarting)
        {
            activities.Add(Create(
                snapshot.ProjectId,
                ActivitySourceKind.Local,
                ActivityPhaseKind.RestartingHost,
                "Launching app",
                utcNow,
                isActive: true));
            return;
        }

        if (StatusPanelBuildVisibilityEvaluator.ShouldShowSiteAwaiting(snapshot))
        {
            activities.Add(Create(
                snapshot.ProjectId,
                ActivitySourceKind.Local,
                ActivityPhaseKind.StartingSite,
                "Starting site",
                utcNow,
                isActive: true,
                detail: snapshot.ListenUrl));
            return;
        }

        if (snapshot.State == ProjectLifecycleState.WaitingForEdits
            || snapshot.IsEditGatingActive)
        {
            activities.Add(Create(
                snapshot.ProjectId,
                ActivitySourceKind.Local,
                ActivityPhaseKind.WaitingForEdits,
                "Waiting for edits",
                utcNow,
                isActive: true,
                detail: snapshot.EditGatingDetailText));
            return;
        }

        if (snapshot.IsActive
            && snapshot.State is ProjectLifecycleState.Building or ProjectLifecycleState.Testing)
        {
            activities.Add(Create(
                snapshot.ProjectId,
                ActivitySourceKind.Local,
                ActivityPhaseKind.Working,
                "Working",
                utcNow,
                isActive: true));
        }
    }

    private static void AddAzure(
        ProjectHealthSnapshot snapshot,
        DateTimeOffset utcNow,
        List<ProjectActivitySnapshot> activities)
    {
        var azure = snapshot.Azure;
        if (azure is null || azure.CiState != AzureCiMonitoringState.Activity)
        {
            return;
        }

        var run = azure.PrimaryRun;
        if (run is null)
        {
            activities.Add(Create(
                snapshot.ProjectId,
                ActivitySourceKind.Azure,
                ActivityPhaseKind.AzureInProgress,
                "Azure · activity",
                utcNow,
                isActive: true,
                detail: azure.StatusMessage,
                branch: azure.FocusBranch));
            return;
        }

        var (phase, verb) = run.State switch
        {
            PipelineRunState.NotStarted => (ActivityPhaseKind.AzureQueued, "queued"),
            PipelineRunState.Canceling => (ActivityPhaseKind.AzureCanceling, "canceling"),
            _ => (ActivityPhaseKind.AzureInProgress, "in progress")
        };

        var pipeline = string.IsNullOrWhiteSpace(run.PipelineDisplayName)
            ? "Azure"
            : run.PipelineDisplayName.Trim();
        var fallbackStatus = $"{pipeline} · {verb}";
        var buildNumberDetail = string.IsNullOrWhiteSpace(run.BuildNumber) ? null : $"#{run.BuildNumber}";

        string status = fallbackStatus;
        string? detail = buildNumberDetail;
        ActivityProgress? progress = null;

        if (azure.ExecutionDetail is { } execution
            && execution.RunId == run.RunId)
        {
            var presentation = AzureRunExecutionProjector.Present(execution);
            if (!string.IsNullOrWhiteSpace(presentation.Summary))
            {
                status = presentation.Summary!;
                detail = presentation.Detail ?? buildNumberDetail;
                progress = presentation.Progress;
            }
        }

        activities.Add(Create(
            snapshot.ProjectId,
            ActivitySourceKind.Azure,
            phase,
            status,
            utcNow,
            isActive: true,
            startedAtUtc: run.StartedAtUtc ?? run.QueuedAtUtc,
            progress: progress,
            operationId: run.RunId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            detail: detail,
            azureRunId: run.RunId,
            azureBuildNumber: run.BuildNumber,
            branch: string.IsNullOrWhiteSpace(run.Branch) ? azure.FocusBranch : run.Branch));
    }

    private static ProjectActivitySnapshot? SelectPrimary(IReadOnlyList<ProjectActivitySnapshot> active)
    {
        if (active.Count == 0)
        {
            return null;
        }

        // Local/agent work answers “what is this machine doing?” first; Azure coexists as secondary.
        return active.FirstOrDefault(a => a.Source is ActivitySourceKind.Local or ActivitySourceKind.Agent)
               ?? active[0];
    }

    private static string? FormatCoexistence(IReadOnlyList<ProjectActivitySnapshot> active)
    {
        if (active.Count < 2)
        {
            return null;
        }

        var local = active.FirstOrDefault(a => a.Source is ActivitySourceKind.Local or ActivitySourceKind.Agent);
        var azure = active.FirstOrDefault(a => a.Source == ActivitySourceKind.Azure);
        if (local is null || azure is null)
        {
            return string.Join(" · ", active.Select(a => a.StatusText));
        }

        return $"{local.StatusText} · {azure.StatusText}";
    }

    /// <summary>
    /// Formats test activity. Pass <paramref name="progress"/> only when both current and total
    /// are trustworthy. Pass <paramref name="completedOnly"/> for authoritative completed counts
    /// when total is still unknown (typical mid-run VSTest console).
    /// </summary>
    public static string FormatTestingStatus(ActivityProgress? progress, int? completedOnly = null)
    {
        if (progress is { Total: > 0 })
        {
            return $"Running tests · {progress.Current} / {progress.Total}";
        }

        if (completedOnly is > 0)
        {
            return $"Running tests · {completedOnly.Value} completed";
        }

        return "Running tests";
    }

    private static (string StatusText, ActivityProgress? Progress, DateTimeOffset? StartedAtUtc)
        ResolveTestingPresentation(ProjectHealthSnapshot snapshot)
    {
        var live = snapshot.TestProgress;
        ActivityProgress? progress = null;
        int? completedOnly = null;
        if (live is not null)
        {
            if (live.Total is > 0)
            {
                progress = ActivityProgress.TryCreate(live.Completed, live.Total.Value);
            }
            else if (live.Completed > 0)
            {
                completedOnly = live.Completed;
            }
        }

        return (FormatTestingStatus(progress, completedOnly), progress, live?.StartedAtUtc);
    }

    private static string AppendTestingProgress(string phaseLabel, ProjectHealthSnapshot snapshot)
    {
        var testing = ResolveTestingPresentation(snapshot);
        if (testing.Progress is { Total: > 0 } p)
        {
            return $"{phaseLabel} · {p.Current} / {p.Total}";
        }

        if (snapshot.TestProgress is { Completed: > 0 } live)
        {
            return $"{phaseLabel} · {live.Completed} completed";
        }

        return phaseLabel;
    }

    private static string FormatActiveBuildStep(string label)
    {
        if (label.Contains("restore", StringComparison.OrdinalIgnoreCase))
        {
            return "Restoring";
        }

        if (label.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            return "Build failed";
        }

        if (label.Contains("publish", StringComparison.OrdinalIgnoreCase))
        {
            return "Publishing";
        }

        var shortName = label.Length > 16 ? label[..14] + "…" : label;
        return $"Compiling {shortName}";
    }

    private static ProjectActivitySnapshot Create(
        string projectId,
        ActivitySourceKind source,
        ActivityPhaseKind phase,
        string statusText,
        DateTimeOffset utcNow,
        bool isActive,
        DateTimeOffset? startedAtUtc = null,
        ActivityProgress? progress = null,
        string? operationId = null,
        string? detail = null,
        long? azureRunId = null,
        string? azureBuildNumber = null,
        string? branch = null) =>
        new(
            projectId,
            source,
            phase,
            statusText,
            utcNow,
            isActive,
            startedAtUtc,
            progress,
            operationId,
            detail,
            azureRunId,
            azureBuildNumber,
            branch);
}
