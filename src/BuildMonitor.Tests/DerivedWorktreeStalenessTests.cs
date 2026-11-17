using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Git;
using BuildMonitor.Infrastructure.Services;

namespace BuildMonitor.Tests;

public sealed class DerivedWorktreeStalenessTests
{
    private const string ParentId = "parent1";
    private const string DerivedRoot = @"C:\src\App-feature";

    private static readonly DerivedWorktreeFolderProbe Healthy = new(FolderExists: true, HasLiveGitWorktree: true);

    [Theory]
    [InlineData(false, false, ParentId, DerivedWorktreeStaleReason.FolderMissing)]
    [InlineData(false, false, "gone", DerivedWorktreeStaleReason.FolderMissing)]
    [InlineData(true, false, ParentId, DerivedWorktreeStaleReason.NotGitWorktree)]
    [InlineData(true, true, "gone", DerivedWorktreeStaleReason.ParentMissing)]
    [InlineData(true, true, ParentId, null)]
    public void Evaluate_classifies_derived_project(
        bool folderExists,
        bool liveWorktree,
        string derivedFrom,
        DerivedWorktreeStaleReason? expected)
    {
        var projects = new List<MonitoredProjectSettings>
        {
            Manual(ParentId, @"C:\src\App"),
            Derived("d1", DerivedRoot, derivedFrom)
        };

        var stale = DerivedWorktreeStaleness.Evaluate(
            projects,
            path => path == DerivedRoot ? new DerivedWorktreeFolderProbe(folderExists, liveWorktree) : Healthy);

        if (expected is null)
        {
            Assert.Empty(stale);
            return;
        }

        var only = Assert.Single(stale);
        Assert.Equal("d1", only.ProjectId);
        Assert.Equal(DerivedRoot, only.RootFolder);
        Assert.Equal(expected, only.Reason);
    }

    [Fact]
    public void Evaluate_never_returns_manual_projects()
    {
        var projects = new List<MonitoredProjectSettings>
        {
            Manual(ParentId, @"C:\src\Missing"),
            Manual("azure-only", rootFolder: null)
        };

        var stale = DerivedWorktreeStaleness.Evaluate(
            projects,
            _ => new DerivedWorktreeFolderProbe(FolderExists: false, HasLiveGitWorktree: false));

        Assert.Empty(stale);
    }

    [Fact]
    public void Prober_reads_git_entries_without_running_git()
    {
        var root = Path.Combine(Path.GetTempPath(), "bm-stale-" + Guid.NewGuid().ToString("N"));
        try
        {
            var primary = Directory.CreateDirectory(Path.Combine(root, "primary")).FullName;
            var adminDir = Directory.CreateDirectory(Path.Combine(primary, ".git", "worktrees", "wt")).FullName;
            var live = Directory.CreateDirectory(Path.Combine(root, "live")).FullName;
            File.WriteAllText(Path.Combine(live, ".git"), $"gitdir: {adminDir}\n");
            var nested = Directory.CreateDirectory(Path.Combine(live, "src", "App")).FullName;
            var pruned = Directory.CreateDirectory(Path.Combine(root, "pruned")).FullName;
            File.WriteAllText(Path.Combine(pruned, ".git"), $"gitdir: {Path.Combine(primary, ".git", "worktrees", "gone")}\n");

            Assert.Equal(Healthy, DerivedWorktreeFolderProber.Probe(primary));
            Assert.Equal(Healthy, DerivedWorktreeFolderProber.Probe(live));
            Assert.Equal(Healthy, DerivedWorktreeFolderProber.Probe(nested));
            Assert.Equal(new DerivedWorktreeFolderProbe(true, false), DerivedWorktreeFolderProber.Probe(pruned));
            Assert.Equal(
                new DerivedWorktreeFolderProbe(false, false),
                DerivedWorktreeFolderProber.Probe(Path.Combine(root, "removed")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_notifies_once_per_newly_stale_project()
    {
        using var scope = new OrchestratorScope();
        var probes = new Dictionary<string, DerivedWorktreeFolderProbe>(StringComparer.OrdinalIgnoreCase);
        scope.Orchestrator.SetDerivedWorktreeFolderProbe(path => probes.GetValueOrDefault(path, Healthy));
        scope.Orchestrator.ApplySettings(new AppSettings
        {
            SchemaVersion = SettingsSchemaV26.Version,
            Projects =
            [
                Manual(ParentId, @"C:\src\App", active: false),
                Derived("d1", @"C:\src\App-a", ParentId),
                Derived("d2", @"C:\src\App-b", ParentId)
            ]
        });

        Assert.Empty(scope.Orchestrator.ScanStaleDerivedWorktrees());
        Assert.Empty(scope.Notifications);

        probes[@"C:\src\App-a"] = new DerivedWorktreeFolderProbe(false, false);
        Assert.Single(scope.Orchestrator.ScanStaleDerivedWorktrees());
        Assert.Single(scope.Notifications);
        Assert.Contains("App (App-a)", scope.Notifications[0]);

        scope.Orchestrator.ScanStaleDerivedWorktrees();
        Assert.Single(scope.Notifications);

        probes[@"C:\src\App-b"] = new DerivedWorktreeFolderProbe(true, false);
        Assert.Equal(2, scope.Orchestrator.ScanStaleDerivedWorktrees().Count);
        Assert.Equal(2, scope.Notifications.Count);
        Assert.Contains("App (App-b)", scope.Notifications[1]);
        Assert.DoesNotContain("App (App-a)", scope.Notifications[1]);
    }

    [Fact]
    public async Task Unregister_stale_removes_only_still_stale_derived_projects()
    {
        using var scope = new OrchestratorScope();
        scope.Orchestrator.SetDerivedWorktreeFolderProbe(path =>
            path == @"C:\src\App-a" ? new DerivedWorktreeFolderProbe(false, false) : Healthy);
        scope.Orchestrator.ApplySettings(new AppSettings
        {
            SchemaVersion = SettingsSchemaV26.Version,
            Projects =
            [
                Manual(ParentId, @"C:\src\App", active: false),
                Derived("d1", @"C:\src\App-a", ParentId),
                Derived("d2", @"C:\src\App-b", ParentId)
            ]
        });

        var outcomes = await scope.Orchestrator.UnregisterStaleDerivedWorktreesAsync(
            [ParentId, "d1", "d2"],
            CancellationToken.None);

        var only = Assert.Single(outcomes);
        Assert.True(only.Ok, only.Error);
        Assert.Equal("d1", only.Worktree.ProjectId);
        Assert.Equal(
            [ParentId, "d2"],
            scope.LastPersisted!.Projects.Select(p => p.Id).ToArray());
    }

    private static MonitoredProjectSettings Manual(string id, string? rootFolder, bool active = true) => new()
    {
        Id = id,
        DisplayName = "App",
        IsActiveInSession = active,
        Local = rootFolder is null
            ? null
            : new LocalProjectAttachment
            {
                RootFolder = rootFolder,
                ProjectFile = "App.csproj",
                RunOptions = new ProjectRunOptions { RunMode = ProjectRunMode.None }
            }
    };

    private static MonitoredProjectSettings Derived(string id, string rootFolder, string derivedFrom) => new()
    {
        Id = id,
        DisplayName = $"App ({Path.GetFileName(rootFolder)})",
        // Inactive so ApplySettings mounts no watcher on paths that do not exist.
        IsActiveInSession = false,
        Local = new LocalProjectAttachment
        {
            RootFolder = rootFolder,
            ProjectFile = "App.csproj",
            DerivedFromProjectId = derivedFrom,
            RunOptions = new ProjectRunOptions { RunMode = ProjectRunMode.None }
        }
    };

    private sealed class OrchestratorScope : IDisposable
    {
        private readonly string root;

        public OrchestratorScope()
        {
            root = Path.Combine(Path.GetTempPath(), "bm-stale-orch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Orchestrator = new ProjectOrchestrator(Path.Combine(root, "logs"), root);
            Orchestrator.SetSettingsPersistHandler(s => LastPersisted = Clone(s));
            Orchestrator.UserNotification += (_, title, message, _, _) =>
                Notifications.Add($"{title}: {message}");
        }

        public ProjectOrchestrator Orchestrator { get; }
        public AppSettings? LastPersisted { get; private set; }
        public List<string> Notifications { get; } = [];

        public void Dispose()
        {
            Orchestrator.Dispose();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup; a lingering log handle must not fail the test.
            }
        }

        private static AppSettings Clone(AppSettings settings) =>
            System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
                System.Text.Json.JsonSerializer.Serialize(settings))!;
    }
}
