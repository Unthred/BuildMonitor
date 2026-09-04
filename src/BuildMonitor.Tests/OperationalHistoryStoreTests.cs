using BuildMonitor.Core.Models;
using BuildMonitor.Infrastructure.Diagnostics;

namespace BuildMonitor.Tests;

public sealed class OperationalHistoryStoreTests
{
    [Fact]
    public void Append_and_read_newest_first()
    {
        using var dir = TempDir.Create();
        var store = NewStore(dir.Path);
        var t0 = DateTimeOffset.Parse("2026-09-04T10:00:00Z");
        var t1 = DateTimeOffset.Parse("2026-09-04T10:01:00Z");

        Assert.True(store.TryRecord(Event("a", "p1", t0, "older")));
        Assert.True(store.TryRecord(Event("b", "p1", t1, "newer")));

        var all = store.GetRecent();
        Assert.Equal(["b", "a"], all.Select(e => e.Id).ToArray());
        Assert.Equal(2, store.GetRecent(limit: 10).Count);
        Assert.Single(store.GetRecent(limit: 1));
        Assert.Equal("b", store.GetRecent(limit: 1)[0].Id);
    }

    [Fact]
    public void Per_project_filtering()
    {
        using var dir = TempDir.Create();
        var store = NewStore(dir.Path);
        var now = DateTimeOffset.UtcNow;
        Assert.True(store.TryRecord(Event("1", "p1", now, "one")));
        Assert.True(store.TryRecord(Event("2", "p2", now.AddSeconds(1), "two")));
        Assert.True(store.TryRecord(Event("3", "p1", now.AddSeconds(2), "three")));

        var p1 = store.GetRecentForProject("p1");
        Assert.Equal(["3", "1"], p1.Select(e => e.Id).ToArray());
        Assert.Equal(["2"], store.GetRecentForProject("P2").Select(e => e.Id).ToArray());
    }

    [Fact]
    public void Restart_restores_retained_events()
    {
        using var dir = TempDir.Create();
        var now = DateTimeOffset.UtcNow;
        var first = NewStore(dir.Path);
        Assert.True(first.TryRecord(Event("id1", "p1", now, "build ok")));
        Assert.True(File.Exists(first.JournalPath));

        var second = NewStore(dir.Path);
        var restored = second.GetRecent();
        Assert.Single(restored);
        Assert.Equal("id1", restored[0].Id);
        Assert.Equal("build ok", restored[0].Summary);
        Assert.Equal(OperationalHistorySchema.CurrentVersion, restored[0].SchemaVersion);
    }

    [Fact]
    public void Age_retention_drops_old_events()
    {
        using var dir = TempDir.Create();
        var store = NewStore(dir.Path, maxAge: TimeSpan.FromDays(3), maxPerProject: 250);
        var now = DateTimeOffset.UtcNow;
        Assert.True(store.TryRecord(Event("old", "p1", now.AddDays(-4), "stale")));
        Assert.True(store.TryRecord(Event("new", "p1", now.AddHours(-1), "fresh")));

        var recent = store.GetRecent();
        Assert.Single(recent);
        Assert.Equal("new", recent[0].Id);
    }

    [Fact]
    public void Count_retention_keeps_newest_per_project()
    {
        using var dir = TempDir.Create();
        var store = NewStore(dir.Path, maxAge: TimeSpan.FromDays(30), maxPerProject: 3);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            Assert.True(store.TryRecord(Event($"p1-{i}", "p1", now.AddMinutes(i), $"e{i}")));
        }

        var p1 = store.GetRecentForProject("p1");
        Assert.Equal(3, p1.Count);
        Assert.Equal(["p1-4", "p1-3", "p1-2"], p1.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void Retention_across_multiple_projects_is_independent()
    {
        using var dir = TempDir.Create();
        var store = NewStore(dir.Path, maxAge: TimeSpan.FromDays(30), maxPerProject: 2);
        var now = DateTimeOffset.UtcNow;
        Assert.True(store.TryRecord(Event("a1", "a", now, "a1")));
        Assert.True(store.TryRecord(Event("a2", "a", now.AddMinutes(1), "a2")));
        Assert.True(store.TryRecord(Event("a3", "a", now.AddMinutes(2), "a3")));
        Assert.True(store.TryRecord(Event("b1", "b", now, "b1")));
        Assert.True(store.TryRecord(Event("b2", "b", now.AddMinutes(1), "b2")));

        Assert.Equal(2, store.GetRecentForProject("a").Count);
        Assert.Equal(2, store.GetRecentForProject("b").Count);
        Assert.Equal(4, store.GetRecent().Count);
    }

    [Fact]
    public void Duplicate_event_id_is_rejected()
    {
        using var dir = TempDir.Create();
        var store = NewStore(dir.Path);
        var now = DateTimeOffset.UtcNow;
        Assert.True(store.TryRecord(Event("same", "p1", now, "first")));
        Assert.False(store.TryRecord(Event("same", "p1", now.AddSeconds(1), "second")));
        Assert.Single(store.GetRecent());
        Assert.Equal("first", store.GetRecent()[0].Summary);
    }

    [Fact]
    public void Schema_version_is_serialized_and_restored()
    {
        using var dir = TempDir.Create();
        var store = NewStore(dir.Path);
        var detail = new OperationalEventDetail(ExitCode: 1, ErrorPreview: "CS0001", LogKind: BuildLogKind.Build);
        var evt = Event("s1", "p1", DateTimeOffset.UtcNow, "failed", detail)
            with
            {
                OperationId = "op1",
                LocalBuildNumber = 7,
                PreviousValue = "Green",
                NewValue = "Red"
            };
        Assert.True(store.TryRecord(evt));

        var line = File.ReadAllText(store.JournalPath);
        Assert.Contains("\"schemaVersion\":1", line, StringComparison.Ordinal);
        Assert.Contains("\"operationId\":\"op1\"", line, StringComparison.Ordinal);

        var restored = NewStore(dir.Path).GetRecent().Single();
        Assert.Equal(1, restored.SchemaVersion);
        Assert.Equal("op1", restored.OperationId);
        Assert.Equal(7, restored.LocalBuildNumber);
        Assert.Equal(1, restored.Detail?.ExitCode);
        Assert.Equal("CS0001", restored.Detail?.ErrorPreview);
    }

    [Fact]
    public void Empty_store_returns_empty()
    {
        using var dir = TempDir.Create();
        var store = NewStore(dir.Path);
        Assert.Empty(store.GetRecent());
        Assert.Empty(store.GetRecentForProject("none"));
        Assert.Empty(store.GetRecent(limit: 5));
    }

    [Fact]
    public void Truncated_final_jsonl_line_is_ignored_and_quarantined()
    {
        using var dir = TempDir.Create();
        var diagnostics = Path.Combine(dir.Path, "diagnostics");
        Directory.CreateDirectory(diagnostics);
        var path = Path.Combine(diagnostics, OperationalHistoryStore.FileName);
        var good = JsonLine(Event("good", "p1", DateTimeOffset.UtcNow, "ok"));
        File.WriteAllText(path, good + Environment.NewLine + "{\"schemaVersion\":1,\"id\":\"bad\"");

        var warnings = new List<string>();
        var store = NewStore(dir.Path, onWarning: warnings.Add);
        Assert.Single(store.GetRecent());
        Assert.Equal("good", store.GetRecent()[0].Id);
        Assert.Contains(warnings, w => w.Contains("truncated", StringComparison.OrdinalIgnoreCase)
            || w.Contains("malformed", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(diagnostics, OperationalHistoryStore.CorruptTailFileName)));

        // Rewrite drops the bad tail so a second load stays clean.
        var again = NewStore(dir.Path);
        Assert.Single(again.GetRecent());
    }

    [Fact]
    public void Middle_file_corruption_skips_bad_line_keeps_neighbours()
    {
        using var dir = TempDir.Create();
        var diagnostics = Path.Combine(dir.Path, "diagnostics");
        Directory.CreateDirectory(diagnostics);
        var path = Path.Combine(diagnostics, OperationalHistoryStore.FileName);
        var t0 = DateTimeOffset.Parse("2026-09-04T10:00:00Z");
        var t1 = DateTimeOffset.Parse("2026-09-04T10:01:00Z");
        var t2 = DateTimeOffset.Parse("2026-09-04T10:02:00Z");
        File.WriteAllLines(path,
        [
            JsonLine(Event("a", "p1", t0, "a")),
            "{not-json",
            JsonLine(Event("c", "p1", t2, "c"))
        ]);

        var store = NewStore(dir.Path);
        var ids = store.GetRecent().Select(e => e.Id).ToArray();
        Assert.Equal(["c", "a"], ids);
        Assert.DoesNotContain("b", ids);
    }

    [Fact]
    public void Persistence_write_failure_does_not_throw_and_keeps_memory()
    {
        using var dir = TempDir.Create();
        var store = NewStore(dir.Path);
        // Create a directory where the journal file should be — AppendAllText will fail.
        var journal = store.JournalPath;
        File.Delete(journal);
        Directory.CreateDirectory(journal);

        var warnings = new List<string>();
        var store2 = new OperationalHistoryStore(
            dir.Path,
            onPersistenceWarning: warnings.Add);

        Assert.True(store2.TryRecord(Event("m1", "p1", DateTimeOffset.UtcNow, "in-memory")));
        Assert.Single(store2.GetRecent());
        Assert.Contains(warnings, w => w.Contains("append", StringComparison.OrdinalIgnoreCase)
            || w.Contains("rewrite", StringComparison.OrdinalIgnoreCase)
            || w.Contains("failed", StringComparison.OrdinalIgnoreCase));

        // Durability was not achieved: a new process cannot restore the in-memory-only event.
        Directory.Delete(journal);
        var afterRestart = NewStore(dir.Path);
        Assert.Empty(afterRestart.GetRecent());
    }

    [Fact]
    public void Duplicate_id_rejected_after_restore_from_disk()
    {
        using var dir = TempDir.Create();
        var now = DateTimeOffset.UtcNow;
        Assert.True(NewStore(dir.Path).TryRecord(Event("restored", "p1", now, "first")));

        var store = NewStore(dir.Path);
        Assert.False(store.TryRecord(Event("restored", "p1", now.AddMinutes(1), "again")));
        Assert.Single(store.GetRecent());
        Assert.Equal("first", store.GetRecent()[0].Summary);
    }

    [Fact]
    public void Failing_test_names_are_clamped_on_record()
    {
        using var dir = TempDir.Create();
        var store = NewStore(dir.Path);
        var names = Enumerable.Range(1, 8).Select(i => $"Test{i}").ToArray();
        var detail = new OperationalEventDetail(FailingTestNames: names);
        Assert.True(store.TryRecord(Event("f1", "p1", DateTimeOffset.UtcNow, "fail", detail)));
        var stored = store.GetRecent().Single().Detail!.FailingTestNames!;
        Assert.Equal(OperationalEventDetail.MaxFailingTestNames, stored.Count);
        Assert.Equal(["Test1", "Test2", "Test3", "Test4", "Test5"], stored.ToArray());
    }

    [Fact]
    public void Default_timestamp_is_rejected()
    {
        using var dir = TempDir.Create();
        var store = NewStore(dir.Path);
        Assert.False(store.TryRecord(Event("t0", "p1", default, "no time")));
        Assert.Empty(store.GetRecent());
    }

    [Fact]
    public void Unknown_schema_version_on_disk_is_skipped()
    {
        using var dir = TempDir.Create();
        var diagnostics = Path.Combine(dir.Path, "diagnostics");
        Directory.CreateDirectory(diagnostics);
        var path = Path.Combine(diagnostics, OperationalHistoryStore.FileName);
        var future = Event("future", "p1", DateTimeOffset.UtcNow, "v99") with { SchemaVersion = 99 };
        var current = Event("now", "p1", DateTimeOffset.UtcNow.AddMinutes(1), "v1");
        File.WriteAllLines(path, [JsonLine(future), JsonLine(current)]);

        var store = NewStore(dir.Path);
        Assert.Single(store.GetRecent());
        Assert.Equal("now", store.GetRecent()[0].Id);
        Assert.Equal(1, store.GetRecent()[0].SchemaVersion);
    }

    [Fact]
    public void Concurrent_append_and_query_are_safe()
    {
        using var dir = TempDir.Create();
        var store = NewStore(dir.Path);
        var now = DateTimeOffset.UtcNow;
        var errors = 0;

        Parallel.For(0, 40, i =>
        {
            try
            {
                store.TryRecord(Event($"c-{i}", "p1", now.AddMilliseconds(i), $"e{i}"));
                _ = store.GetRecent(limit: 10);
                _ = store.GetRecentForProject("p1", limit: 5);
            }
            catch
            {
                Interlocked.Increment(ref errors);
            }
        });

        Assert.Equal(0, errors);
        Assert.Equal(40, store.GetRecent().Count);
    }

    [Fact]
    public void Compaction_rewrite_does_not_duplicate_on_reload()
    {
        using var dir = TempDir.Create();
        var store = NewStore(dir.Path, maxAge: TimeSpan.FromDays(30), maxPerProject: 2);
        var now = DateTimeOffset.UtcNow;
        Assert.True(store.TryRecord(Event("1", "p1", now, "1")));
        Assert.True(store.TryRecord(Event("2", "p1", now.AddMinutes(1), "2")));
        Assert.True(store.TryRecord(Event("3", "p1", now.AddMinutes(2), "3"))); // triggers retention rewrite

        var lines = File.ReadAllLines(store.JournalPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        Assert.Equal(2, lines.Length);

        var reloaded = NewStore(dir.Path, maxAge: TimeSpan.FromDays(30), maxPerProject: 2);
        Assert.Equal(2, reloaded.GetRecent().Count);
        Assert.Equal(["3", "2"], reloaded.GetRecent().Select(e => e.Id).ToArray());
    }

    [Fact]
    public void Invalid_schema_or_blank_fields_rejected()
    {
        using var dir = TempDir.Create();
        var store = NewStore(dir.Path);
        var now = DateTimeOffset.UtcNow;
        Assert.False(store.TryRecord(Event("x", "p1", now, "bad") with { SchemaVersion = 99 }));
        Assert.False(store.TryRecord(Event(" ", "p1", now, "bad")));
        Assert.False(store.TryRecord(Event("y", "", now, "bad")));
        Assert.False(store.TryRecord(Event("z", "p1", now, "")));
        Assert.Empty(store.GetRecent());
    }

    private static OperationalHistoryStore NewStore(
        string appData,
        TimeSpan? maxAge = null,
        int maxPerProject = OperationalHistoryStore.DefaultMaxEventsPerProject,
        Action<string>? onWarning = null) =>
        new(appData, maxAge, maxPerProject, onWarning);

    private static OperationalEvent Event(
        string id,
        string projectId,
        DateTimeOffset at,
        string summary,
        OperationalEventDetail? detail = null) =>
        new(
            OperationalHistorySchema.CurrentVersion,
            id,
            projectId,
            at,
            OperationalEventSource.Local,
            OperationalEventKind.Build,
            OperationalEventOutcome.Succeeded,
            summary,
            detail);

    private static string JsonLine(OperationalEvent evt) =>
        System.Text.Json.JsonSerializer.Serialize(
            evt,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        private TempDir(string path) => Path = path;

        public static TempDir Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "BuildMonitor-OpsHistory-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDir(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // best effort
            }
        }
    }
}
