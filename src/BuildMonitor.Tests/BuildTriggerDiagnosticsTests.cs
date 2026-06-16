using BuildMonitor.Core.Models;
using BuildMonitor.Infrastructure.Diagnostics;

namespace BuildMonitor.Tests;

public class BuildTriggerKindFormatterTests
{
    [Theory]
    [InlineData("startup", false, BuildTriggerKind.SessionStart)]
    [InlineData("manual rebuild", false, BuildTriggerKind.ManualRebuild)]
    [InlineData("hot reload rebuild", false, BuildTriggerKind.HotReloadRebuild)]
    [InlineData("file change", true, BuildTriggerKind.FileWatcher)]
    [InlineData("file change (queued)", true, BuildTriggerKind.FileWatcherQueued)]
    public void FromBuildReason_maps_known_reasons(string reason, bool fileChange, BuildTriggerKind expected) =>
        Assert.Equal(expected, BuildTriggerKindFormatter.FromBuildReason(reason, fileChange));
}

public class BuildTriggerJournalTests : IDisposable
{
    private readonly string root;

    public BuildTriggerJournalTests()
    {
        root = Path.Combine(Path.GetTempPath(), "BuildMonitor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [Fact]
    public void Record_persists_and_loads_entries()
    {
        var journal = new BuildTriggerJournal(root);
        journal.Record(new BuildTriggerRecord(
            "a1",
            "proj",
            "My App",
            DateTimeOffset.UtcNow,
            BuildTriggerKind.FileWatcher,
            "file change",
            Detail: null,
            ChangedPaths: ["Pages/Index.razor"]));

        var reloaded = new BuildTriggerJournal(root);
        var entries = reloaded.GetEntries();

        Assert.Single(entries);
        Assert.Equal(BuildTriggerKind.FileWatcher, entries[0].Kind);
        Assert.Equal("Pages/Index.razor", entries[0].ChangedPaths![0]);
    }

    [Fact]
    public void SetVerdict_updates_entry()
    {
        var journal = new BuildTriggerJournal(root);
        journal.Record(new BuildTriggerRecord(
            "b2",
            "proj",
            "My App",
            DateTimeOffset.UtcNow,
            BuildTriggerKind.SessionStart,
            "startup"));

        journal.SetVerdict("b2", BuildTriggerVerdict.Unexpected);

        Assert.Equal(BuildTriggerVerdict.Unexpected, journal.GetEntries()[0].Verdict);
    }

    [Fact]
    public void Infer_flags_agent_tooling_paths()
    {
        var cause = BuildTriggerInference.Infer(
            BuildTriggerKind.FileWatcher,
            null,
            [".cursor/plans/foo.md"]);

        Assert.Contains("agent", cause, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Infer_notes_missing_paths_for_file_watcher()
    {
        var cause = BuildTriggerInference.Infer(BuildTriggerKind.FileWatcher, null, null);

        Assert.Contains("no captured paths", cause, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
