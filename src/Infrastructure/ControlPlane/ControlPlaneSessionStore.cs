using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Infrastructure.ControlPlane;

/// <summary>Per-project agent session busy/idle state for the loopback control plane.</summary>
public sealed class ControlPlaneSessionStore
{
    private readonly object sync = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ControlPlaneMetricsStore? metrics;
    private int busyTimeoutSeconds = 120;
    private bool suppressAutoBuildTestsDefault = true;

    public ControlPlaneSessionStore(ControlPlaneMetricsStore? metrics = null) =>
        this.metrics = metrics;

    public void ApplyMonitorDefaults(int busyTimeoutSeconds, bool suppressAutoBuildTestsDefault)
    {
        lock (sync)
        {
            this.busyTimeoutSeconds = Math.Clamp(busyTimeoutSeconds, 30, 3600);
            this.suppressAutoBuildTestsDefault = suppressAutoBuildTestsDefault;
        }
    }

    public ControlPlaneSessionStatus GetStatus(string projectId, DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        TimeSpan? expiredBusy = null;
        ControlPlaneSessionStatus status;
        lock (sync)
        {
            if (!entries.TryGetValue(projectId, out var entry))
            {
                return new ControlPlaneSessionStatus(
                    ControlPlaneSessionState.Idle,
                    now,
                    SessionApiUsed: false,
                    SuppressAutoBuildTests: suppressAutoBuildTestsDefault);
            }

            var effective = ControlPlaneSessionPolicy.ResolveEffectiveState(
                entry.State,
                entry.LastActivityUtc,
                busyTimeoutSeconds,
                now);
            if (effective == ControlPlaneSessionState.Idle && entry.State == ControlPlaneSessionState.Busy)
            {
                expiredBusy = now - entry.SinceUtc;
                entry.State = ControlPlaneSessionState.Idle;
                entry.SinceUtc = now;
                entry.LastActivityUtc = now;
                entry.IdleCause = ControlPlaneIdleCause.Timeout;
            }

            status = SnapshotLocked(entry, now);
        }

        if (expiredBusy is { } duration)
        {
            metrics?.RecordBusyInterval(projectId, duration);
        }

        return status;
    }

    public ControlPlaneSessionStatus MarkBusy(string projectId, bool? suppressAutoBuildTests = null)
    {
        var now = DateTimeOffset.UtcNow;
        ControlPlaneSessionStatus status;
        lock (sync)
        {
            var entry = GetOrCreate(projectId, now);
            entry.SessionApiUsed = true;
            entry.State = ControlPlaneSessionState.Busy;
            entry.SinceUtc = now;
            entry.LastActivityUtc = now;
            entry.IdleCause = ControlPlaneIdleCause.None;
            if (suppressAutoBuildTests.HasValue)
            {
                entry.SuppressAutoBuildTestsOverride = suppressAutoBuildTests;
            }

            status = SnapshotLocked(entry, now);
        }

        metrics?.MarkSessionApiUsed(projectId);
        return status;
    }

    /// <summary>
    /// Extends the busy timeout without restarting the visible busy duration.
    /// No-op when the project is not effectively busy.
    /// </summary>
    public ControlPlaneSessionStatus TouchBusy(string projectId, DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        var status = GetStatus(projectId, now);
        if (status.State != ControlPlaneSessionState.Busy)
        {
            return status;
        }

        lock (sync)
        {
            if (!entries.TryGetValue(projectId, out var entry)
                || entry.State != ControlPlaneSessionState.Busy)
            {
                return status;
            }

            entry.LastActivityUtc = now;
            return SnapshotLocked(entry, now);
        }
    }

    public ControlPlaneSessionStatus MarkIdle(string projectId, bool? suppressAutoBuildTests = null)
    {
        var now = DateTimeOffset.UtcNow;
        TimeSpan? busyDuration = null;
        ControlPlaneSessionStatus status;
        lock (sync)
        {
            var entry = GetOrCreate(projectId, now);
            if (entry.State == ControlPlaneSessionState.Busy)
            {
                busyDuration = now - entry.SinceUtc;
            }

            entry.SessionApiUsed = true;
            entry.State = ControlPlaneSessionState.Idle;
            entry.SinceUtc = now;
            entry.LastActivityUtc = now;
            entry.IdleCause = ControlPlaneIdleCause.Agent;
            if (suppressAutoBuildTests.HasValue)
            {
                entry.SuppressAutoBuildTestsOverride = suppressAutoBuildTests;
            }

            status = SnapshotLocked(entry, now);
        }

        metrics?.MarkSessionApiUsed(projectId);
        if (busyDuration is { } duration)
        {
            metrics?.RecordBusyInterval(projectId, duration);
        }

        return status;
    }

    public ControlPlaneSessionStatus SetSuppressAutoBuildTests(string projectId, bool suppressAutoBuildTests)
    {
        var now = DateTimeOffset.UtcNow;
        lock (sync)
        {
            var entry = GetOrCreate(projectId, now);
            entry.SessionApiUsed = true;
            entry.SuppressAutoBuildTestsOverride = suppressAutoBuildTests;
            return SnapshotLocked(entry, now);
        }
    }

    public bool ShouldBlockAutoBuild(string projectId, DateTimeOffset? utcNow = null)
    {
        var status = GetStatus(projectId, utcNow);
        return ControlPlaneSessionPolicy.ShouldBlockAutoBuild(status.SessionApiUsed, status.State);
    }

    public bool ShouldSuppressAutoBuildTests(string projectId)
    {
        var status = GetStatus(projectId);
        return status.SuppressAutoBuildTests;
    }

    private Entry GetOrCreate(string projectId, DateTimeOffset now)
    {
        if (entries.TryGetValue(projectId, out var existing))
        {
            return existing;
        }

        var created = new Entry
        {
            State = ControlPlaneSessionState.Idle,
            SinceUtc = now,
            LastActivityUtc = now,
            SessionApiUsed = false,
            IdleCause = ControlPlaneIdleCause.None
        };
        entries[projectId] = created;
        return created;
    }

    private ControlPlaneSessionStatus SnapshotLocked(Entry entry, DateTimeOffset now)
    {
        var effective = ControlPlaneSessionPolicy.ResolveEffectiveState(
            entry.State,
            entry.LastActivityUtc,
            busyTimeoutSeconds,
            now);
        return new ControlPlaneSessionStatus(
            effective,
            entry.SinceUtc,
            entry.SessionApiUsed,
            ControlPlaneSessionPolicy.ResolveSuppressAutoBuildTests(
                entry.SuppressAutoBuildTestsOverride,
                suppressAutoBuildTestsDefault),
            effective == ControlPlaneSessionState.Busy
                ? ControlPlaneIdleCause.None
                : entry.IdleCause,
            entry.LastActivityUtc);
    }

    private sealed class Entry
    {
        public ControlPlaneSessionState State { get; set; }
        public DateTimeOffset SinceUtc { get; set; }
        public DateTimeOffset LastActivityUtc { get; set; }
        public bool SessionApiUsed { get; set; }
        public bool? SuppressAutoBuildTestsOverride { get; set; }
        public ControlPlaneIdleCause IdleCause { get; set; }
    }
}
