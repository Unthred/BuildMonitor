using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.ControlPlane;

/// <summary>In-memory, process-lifetime metrics for the loopback control plane.</summary>
public sealed class ControlPlaneMetricsStore
{
    private readonly object sync = new();
    private readonly Dictionary<string, ProjectCounters> byProject = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DateTimeOffset> globalCallTimes = new();

    public void RecordHttp(string? projectId, string routeKey, int statusCode, TimeSpan? duration = null)
    {
        var now = DateTimeOffset.UtcNow;
        lock (sync)
        {
            PruneCallTimes(now);
            globalCallTimes.Enqueue(now);

            if (string.IsNullOrWhiteSpace(projectId))
            {
                return;
            }

            var c = GetOrCreate(projectId);
            c.HttpRequests++;
            c.CallTimes.Enqueue(now);
            if (statusCode is >= 400 and < 500)
            {
                c.HttpClientErrors++;
            }
            else if (statusCode >= 500)
            {
                c.HttpServerErrors++;
            }

            switch (routeKey)
            {
                case "session/busy" when statusCode is >= 200 and < 300:
                    c.BusyCalls++;
                    c.LastBusyUtc = now;
                    break;
                case "session/idle" when statusCode is >= 200 and < 300:
                    c.IdleCalls++;
                    c.LastIdleUtc = now;
                    break;
                case "watch/pause" when statusCode is >= 200 and < 300:
                    c.WatchPauseCalls++;
                    break;
                case "watch/resume" when statusCode is >= 200 and < 300:
                    c.WatchResumeCalls++;
                    break;
            }

            if (duration is { } d && routeKey == "run/ship-check" && statusCode is >= 200 and < 300)
            {
                // Duration recorded separately via RecordShipCheck when result is known.
                _ = d;
            }
        }
    }

    public void RecordBusyInterval(string projectId, TimeSpan busyDuration)
    {
        if (busyDuration <= TimeSpan.Zero || string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        lock (sync)
        {
            var c = GetOrCreate(projectId);
            c.TotalBusyMs += (long)busyDuration.TotalMilliseconds;
        }
    }

    public void RecordShipCheck(string projectId, bool ok, TimeSpan duration)
    {
        var now = DateTimeOffset.UtcNow;
        lock (sync)
        {
            var c = GetOrCreate(projectId);
            c.ShipCheckTotal++;
            if (ok)
            {
                c.ShipCheckPassed++;
            }
            else
            {
                c.ShipCheckFailed++;
            }

            c.ShipCheckDurationMs.Add((int)Math.Clamp(duration.TotalMilliseconds, 0, int.MaxValue));
            if (c.ShipCheckDurationMs.Count > 40)
            {
                c.ShipCheckDurationMs.RemoveAt(0);
            }

            c.LastShipCheckUtc = now;
            c.LastShipCheckOk = ok;
        }
    }

    public void RecordAutoBuildBlocked(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        lock (sync)
        {
            GetOrCreate(projectId).AutoBuildsBlocked++;
        }
    }

    public void MarkSessionApiUsed(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        lock (sync)
        {
            GetOrCreate(projectId).SessionApiUsed = true;
        }
    }

    public ControlPlaneMetricsSnapshot GetSnapshot(
        string projectId,
        ControlPlaneSessionStatus? session = null,
        DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        lock (sync)
        {
            PruneCallTimes(now);
            if (!byProject.TryGetValue(projectId, out var c))
            {
                var empty = ControlPlaneMetricsSnapshot.Empty(projectId);
                if (session is { SessionApiUsed: true })
                {
                    return empty with
                    {
                        SessionApiUsed = true,
                        SessionStateText = session.State.ToString().ToLowerInvariant()
                    };
                }

                return empty;
            }

            PruneProjectCallTimes(c, now);
            var callsLastHour = c.CallTimes.Count;
            var rate = callsLastHour; // already last-hour window
            var successRate = c.ShipCheckTotal == 0
                ? "—"
                : $"{(100.0 * c.ShipCheckPassed / c.ShipCheckTotal):0.#}%";
            var avgMs = c.ShipCheckDurationMs.Count == 0
                ? (int?)null
                : (int)c.ShipCheckDurationMs.Average();
            var stateText = session is null
                ? (c.SessionApiUsed ? "unknown" : "idle (no session API yet)")
                : session.SessionApiUsed
                    ? session.State.ToString().ToLowerInvariant()
                    : "idle (no session API yet)";

            var summary = c.HttpRequests == 0 && c.ShipCheckTotal == 0 && c.AutoBuildsBlocked == 0
                ? "No control-plane calls yet this process."
                : $"{c.HttpRequests} HTTP · {c.BusyCalls} busy / {c.IdleCalls} idle · "
                  + $"ship {c.ShipCheckPassed}/{c.ShipCheckTotal} · blocked builds {c.AutoBuildsBlocked}";

            return new ControlPlaneMetricsSnapshot(
                projectId,
                c.SessionApiUsed || session?.SessionApiUsed == true,
                stateText,
                c.BusyCalls,
                c.IdleCalls,
                FormatDuration(c.TotalBusyMs),
                FormatLocal(c.LastBusyUtc),
                FormatLocal(c.LastIdleUtc),
                c.AutoBuildsBlocked,
                c.ShipCheckTotal,
                c.ShipCheckPassed,
                c.ShipCheckFailed,
                successRate,
                avgMs is null ? "—" : FormatDuration(avgMs.Value),
                FormatLocal(c.LastShipCheckUtc),
                c.LastShipCheckUtc is null ? "—" : (c.LastShipCheckOk == true ? "pass" : "fail"),
                c.WatchPauseCalls,
                c.WatchResumeCalls,
                c.HttpRequests,
                c.HttpClientErrors,
                c.HttpServerErrors,
                callsLastHour,
                $"{rate} / h",
                summary);
        }
    }

    private ProjectCounters GetOrCreate(string projectId)
    {
        if (byProject.TryGetValue(projectId, out var existing))
        {
            return existing;
        }

        var created = new ProjectCounters();
        byProject[projectId] = created;
        return created;
    }

    private void PruneCallTimes(DateTimeOffset now)
    {
        var cutoff = now.AddHours(-1);
        while (globalCallTimes.Count > 0 && globalCallTimes.Peek() < cutoff)
        {
            globalCallTimes.Dequeue();
        }
    }

    private static void PruneProjectCallTimes(ProjectCounters c, DateTimeOffset now)
    {
        var cutoff = now.AddHours(-1);
        while (c.CallTimes.Count > 0 && c.CallTimes.Peek() < cutoff)
        {
            c.CallTimes.Dequeue();
        }
    }

    private static string FormatLocal(DateTimeOffset? utc) =>
        utc is null ? "—" : utc.Value.ToLocalTime().ToString("t");

    private static string FormatDuration(long ms)
    {
        if (ms <= 0)
        {
            return "—";
        }

        if (ms < 1000)
        {
            return $"{ms} ms";
        }

        var seconds = ms / 1000.0;
        if (seconds < 60)
        {
            return $"{seconds:0.#} s";
        }

        var minutes = seconds / 60.0;
        return minutes < 60 ? $"{minutes:0.#} min" : $"{minutes / 60.0:0.#} h";
    }

    private sealed class ProjectCounters
    {
        public bool SessionApiUsed { get; set; }
        public int BusyCalls { get; set; }
        public int IdleCalls { get; set; }
        public long TotalBusyMs { get; set; }
        public DateTimeOffset? LastBusyUtc { get; set; }
        public DateTimeOffset? LastIdleUtc { get; set; }
        public int AutoBuildsBlocked { get; set; }
        public int ShipCheckTotal { get; set; }
        public int ShipCheckPassed { get; set; }
        public int ShipCheckFailed { get; set; }
        public List<int> ShipCheckDurationMs { get; } = [];
        public DateTimeOffset? LastShipCheckUtc { get; set; }
        public bool? LastShipCheckOk { get; set; }
        public int WatchPauseCalls { get; set; }
        public int WatchResumeCalls { get; set; }
        public int HttpRequests { get; set; }
        public int HttpClientErrors { get; set; }
        public int HttpServerErrors { get; set; }
        public Queue<DateTimeOffset> CallTimes { get; } = new();
    }
}
