using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Abstractions;

/// <summary>
/// Bounded, append-only operational history (#110 / #113).
/// Observability only — must not gate build/run/test execution.
/// </summary>
public interface IOperationalHistoryStore
{
    /// <summary>
    /// Accepts an event into <b>current-session</b> operational history when valid and not a duplicate id.
    /// Returns <c>true</c> when the event is available via <see cref="GetRecent"/> / <see cref="GetRecentForProject"/>
    /// for this process.
    /// <para>
    /// Disk persistence is <b>best-effort</b>: a write failure does not throw and does not change the
    /// <c>true</c> result. The event may then be absent after restart; durability issues are reported
    /// only via diagnostics/warnings, never by failing runtime work.
    /// </para>
    /// </summary>
    bool TryRecord(OperationalEvent entry);

    /// <summary>Newest-first recent events across all projects.</summary>
    IReadOnlyList<OperationalEvent> GetRecent(int? limit = null);

    /// <summary>Newest-first recent events for one project.</summary>
    IReadOnlyList<OperationalEvent> GetRecentForProject(string projectId, int? limit = null);
}
