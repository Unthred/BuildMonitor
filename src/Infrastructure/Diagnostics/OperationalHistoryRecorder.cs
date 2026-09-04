using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.Diagnostics;

/// <summary>
/// Small factory/recorder for operational history (#114).
/// Never throws into callers; <see cref="IOperationalHistoryStore.TryRecord"/> remains best-effort.
/// </summary>
public static class OperationalHistoryRecorder
{
    public static string NewOperationId() => Guid.NewGuid().ToString("N");

    public static bool TryRecord(IOperationalHistoryStore? store, OperationalEvent entry)
    {
        if (store is null)
        {
            return false;
        }

        try
        {
            return store.TryRecord(entry);
        }
        catch
        {
            return false;
        }
    }

    public static OperationalEvent Create(
        string projectId,
        OperationalEventSource source,
        OperationalEventKind kind,
        OperationalEventOutcome outcome,
        string summary,
        string? operationId = null,
        string? buildTriggerId = null,
        int? localBuildNumber = null,
        long? azureRunId = null,
        string? azureBuildNumber = null,
        string? branch = null,
        string? previousValue = null,
        string? newValue = null,
        OperationalEventDetail? detail = null,
        DateTimeOffset? occurredAtUtc = null) =>
        new(
            OperationalHistorySchema.CurrentVersion,
            Guid.NewGuid().ToString("N"),
            projectId,
            occurredAtUtc ?? DateTimeOffset.UtcNow,
            source,
            kind,
            outcome,
            summary,
            detail,
            operationId,
            buildTriggerId,
            localBuildNumber,
            azureRunId,
            azureBuildNumber,
            branch,
            previousValue,
            newValue);
}
