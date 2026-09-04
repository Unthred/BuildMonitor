using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.Diagnostics;

/// <summary>
/// Per-runtime operation correlation + consistent event construction for operational history (#114).
/// Build/test gates serialize per project, so a single active <see cref="OperationId"/> is safe.
/// Ownership:
/// <list type="bullet">
/// <item><see cref="TryBeginCallerOwnedOperation"/> — cleared by the caller (tray/control-plane) with matching id.</item>
/// <item><see cref="BeginRuntimeOwnedOperation"/> — cleared by build/test finally when the unit ends.</item>
/// </list>
/// </summary>
internal sealed class OperationalHistoryEmitter
{
    private readonly IOperationalHistoryStore? store;
    private readonly Func<string> projectId;
    private string? activeOperationId;
    private OperationOwnership ownership = OperationOwnership.None;
    private ProjectLifecycleState? lastWaitingEdgeState;
    private bool suppressHostStartStopForRestart;

    public OperationalHistoryEmitter(IOperationalHistoryStore? store, Func<string> projectId)
    {
        this.store = store;
        this.projectId = projectId;
    }

    public string? OperationId => activeOperationId;

    public bool HasActiveOperation => !string.IsNullOrEmpty(activeOperationId);

    /// <summary>
    /// Starts a caller-owned operation only when none is active.
    /// Returns <c>false</c> without mutating correlation when another operation owns the slot
    /// (prevents overlapping actions from stealing <see cref="OperationId"/>).
    /// </summary>
    public bool TryBeginCallerOwnedOperation(
        OperationalEventSource source,
        string actionName,
        string summary,
        out string operationId)
    {
        if (!string.IsNullOrEmpty(activeOperationId))
        {
            operationId = activeOperationId;
            return false;
        }

        operationId = OperationalHistoryRecorder.NewOperationId();
        activeOperationId = operationId;
        ownership = OperationOwnership.Caller;
        RecordExplicit(source, actionName, summary, OperationalEventOutcome.Started);
        return true;
    }

    public string BeginRuntimeOwnedOperation(
        OperationalEventSource source,
        string actionName,
        string summary,
        bool recordExplicitAction)
    {
        if (!string.IsNullOrEmpty(activeOperationId))
        {
            return activeOperationId;
        }

        var id = OperationalHistoryRecorder.NewOperationId();
        activeOperationId = id;
        ownership = OperationOwnership.Runtime;
        if (recordExplicitAction)
        {
            RecordExplicit(source, actionName, summary, OperationalEventOutcome.Started);
        }

        return id;
    }

    /// <summary>Creates a runtime-owned operation when none is active (file-triggered / ambient).</summary>
    public string EnsureRuntimeOperation(
        OperationalEventSource source,
        string actionName,
        string summary,
        bool recordExplicitAction)
    {
        if (!string.IsNullOrEmpty(activeOperationId))
        {
            return activeOperationId;
        }

        return BeginRuntimeOwnedOperation(source, actionName, summary, recordExplicitAction);
    }

    /// <summary>
    /// Clears the caller-owned slot only when <paramref name="expectedOperationId"/> matches
    /// the active id. A null id is a no-op so a rejected overlap's <c>finally</c> cannot end
    /// another unit's correlation.
    /// </summary>
    public void ClearCallerOwnedOperation(string? expectedOperationId)
    {
        if (ownership != OperationOwnership.Caller
            || expectedOperationId is null
            || !string.Equals(activeOperationId, expectedOperationId, StringComparison.Ordinal))
        {
            return;
        }

        activeOperationId = null;
        ownership = OperationOwnership.None;
    }

    public void ClearRuntimeOwnedOperation()
    {
        if (ownership == OperationOwnership.Runtime)
        {
            activeOperationId = null;
            ownership = OperationOwnership.None;
        }
    }

    public void RecordExplicit(
        OperationalEventSource source,
        string actionName,
        string summary,
        OperationalEventOutcome outcome,
        string? previousValue = null,
        string? newValue = null,
        OperationalEventDetail? extraDetail = null)
    {
        var detail = MergeActionName(extraDetail, actionName);
        TryRecord(
            source,
            OperationalEventKind.ExplicitAction,
            outcome,
            summary,
            detail,
            previousValue,
            newValue);
    }

    public void RecordWorkflowModeChange(
        OperationalEventSource source,
        string previousWire,
        string newWire)
    {
        TryRecord(
            source,
            OperationalEventKind.WorkflowMode,
            OperationalEventOutcome.Changed,
            $"Build-control mode → {newWire}",
            new OperationalEventDetail(ActionName: "build-control-mode"),
            previousWire,
            newWire);
    }

    public void RecordWaitingForEditsEntered(string summary)
    {
        if (lastWaitingEdgeState == ProjectLifecycleState.WaitingForEdits)
        {
            return;
        }

        lastWaitingEdgeState = ProjectLifecycleState.WaitingForEdits;
        TryRecord(
            OperationalEventSource.System,
            OperationalEventKind.WaitingForEdits,
            OperationalEventOutcome.Started,
            summary,
            new OperationalEventDetail(ActionName: "waiting-for-edits", HoldReason: summary));
    }

    public void NoteLifecycleState(ProjectLifecycleState state)
    {
        if (state != ProjectLifecycleState.WaitingForEdits
            && lastWaitingEdgeState == ProjectLifecycleState.WaitingForEdits)
        {
            lastWaitingEdgeState = state;
            TryRecord(
                OperationalEventSource.System,
                OperationalEventKind.WaitingForEdits,
                OperationalEventOutcome.Succeeded,
                "Left WaitingForEdits",
                new OperationalEventDetail(ActionName: "waiting-for-edits-left"));
        }
        else if (state == ProjectLifecycleState.WaitingForEdits)
        {
            lastWaitingEdgeState = ProjectLifecycleState.WaitingForEdits;
        }
    }

    public void RecordBuild(
        OperationalEventOutcome outcome,
        string summary,
        string? buildTriggerId,
        int localBuildNumber,
        string? branch,
        OperationalEventDetail? detail)
    {
        TryRecord(
            OperationalEventSource.Local,
            OperationalEventKind.Build,
            outcome,
            summary,
            detail,
            buildTriggerId: buildTriggerId,
            localBuildNumber: localBuildNumber,
            branch: branch);
    }

    public void RecordTests(
        OperationalEventOutcome outcome,
        string summary,
        OperationalEventDetail? detail)
    {
        TryRecord(
            OperationalEventSource.Local,
            OperationalEventKind.Tests,
            outcome,
            summary,
            detail);
    }

    public void BeginIntentionalRestart()
    {
        suppressHostStartStopForRestart = true;
    }

    public void CompleteIntentionalRestart(bool hostStarted)
    {
        if (!suppressHostStartStopForRestart)
        {
            return;
        }

        suppressHostStartStopForRestart = false;
        if (hostStarted)
        {
            TryRecord(
                OperationalEventSource.Local,
                OperationalEventKind.RunHost,
                OperationalEventOutcome.Succeeded,
                "Host restarted",
                new OperationalEventDetail(ActionName: "host-restarted"));
        }
    }

    public void CancelIntentionalRestartSuppression()
    {
        suppressHostStartStopForRestart = false;
    }

    public void RecordHostStarted(string summary)
    {
        if (suppressHostStartStopForRestart)
        {
            return;
        }

        TryRecord(
            OperationalEventSource.Local,
            OperationalEventKind.RunHost,
            OperationalEventOutcome.Started,
            summary,
            new OperationalEventDetail(ActionName: "host-started"));
    }

    public void RecordHostStopped(string summary)
    {
        if (suppressHostStartStopForRestart)
        {
            return;
        }

        TryRecord(
            OperationalEventSource.Local,
            OperationalEventKind.RunHost,
            OperationalEventOutcome.Succeeded,
            summary,
            new OperationalEventDetail(ActionName: "host-stopped"));
    }

    public void RecordHostCrashed(string summary, int? exitCode, string? errorPreview)
    {
        TryRecord(
            OperationalEventSource.Local,
            OperationalEventKind.RunHost,
            OperationalEventOutcome.Failed,
            summary,
            new OperationalEventDetail(
                ExitCode: exitCode,
                ErrorPreview: TruncatePreview(errorPreview),
                ActionName: "host-crashed"));
    }

    private void TryRecord(
        OperationalEventSource source,
        OperationalEventKind kind,
        OperationalEventOutcome outcome,
        string summary,
        OperationalEventDetail? detail = null,
        string? previousValue = null,
        string? newValue = null,
        string? buildTriggerId = null,
        int? localBuildNumber = null,
        string? branch = null)
    {
        OperationalHistoryRecorder.TryRecord(
            store,
            OperationalHistoryRecorder.Create(
                projectId(),
                source,
                kind,
                outcome,
                summary,
                operationId: activeOperationId,
                buildTriggerId: buildTriggerId,
                localBuildNumber: localBuildNumber,
                branch: branch,
                previousValue: previousValue,
                newValue: newValue,
                detail: detail));
    }

    private static OperationalEventDetail MergeActionName(OperationalEventDetail? detail, string actionName)
    {
        if (detail is null)
        {
            return new OperationalEventDetail(ActionName: actionName);
        }

        return detail with { ActionName = detail.ActionName ?? actionName };
    }

    private static string? TruncatePreview(string? preview)
    {
        if (string.IsNullOrWhiteSpace(preview))
        {
            return null;
        }

        const int max = 240;
        var trimmed = preview.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private enum OperationOwnership
    {
        None = 0,
        Caller = 1,
        Runtime = 2
    }
}
