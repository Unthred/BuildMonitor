using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Services;

namespace BuildMonitor.Tests;

public sealed class ProjectStartConcurrencyLimitTests
{
    [Theory]
    [InlineData(7, 0, 5, 5, 2)]
    [InlineData(3, 0, 5, 3, 0)]
    [InlineData(3, 4, 5, 1, 2)]
    [InlineData(2, 5, 5, 0, 2)]
    [InlineData(2, 9, 5, 0, 2)]
    [InlineData(0, 0, 5, 0, 0)]
    public void Split_takes_free_slots_in_order(
        int candidates,
        int alreadyStarted,
        int limit,
        int expectedStart,
        int expectedDeferred)
    {
        var items = Enumerable.Range(1, candidates).ToList();

        var partition = ProjectStartConcurrencyLimit.Split(items, alreadyStarted, limit);

        Assert.Equal(expectedStart, partition.Start.Count);
        Assert.Equal(expectedDeferred, partition.Deferred.Count);
        Assert.Equal(items, partition.Start.Concat(partition.Deferred));
    }

    [Theory]
    [InlineData(5, "Not started: limit of 5 active projects reached")]
    [InlineData(1, "Not started: limit of 1 active project reached")]
    public void Reason_names_the_limit(int limit, string expected) =>
        Assert.Equal(expected, ProjectStartConcurrencyLimit.FormatNotStartedReason(limit));

    [Fact]
    public void Notification_names_held_back_projects()
    {
        Assert.Equal("2 projects not started", ProjectStartConcurrencyLimit.FormatNotificationTitle(2));
        var message = ProjectStartConcurrencyLimit.FormatNotificationMessage(["inbox-phase1", "AB782"], 5);
        Assert.StartsWith("inbox-phase1, AB782: limit of 5 active projects reached.", message);
        Assert.Contains("Start", message);
    }

    [Fact]
    public async Task Launch_over_limit_marks_rest_not_started_and_notifies_once()
    {
        using var scope = new Scope();
        scope.Apply(limit: 2, "a", "b", "c", "d");

        await scope.Orchestrator.StartActiveProjectsAsync(CancellationToken.None);

        Assert.Equal(["a", "b"], scope.Started);
        Assert.Equal(["a", "b"], scope.Orchestrator.GetStartedProjectIds().Order());
        Assert.Null(scope.Reason("a"));
        Assert.Equal("Not started: limit of 2 active projects reached", scope.Reason("c"));
        Assert.Equal("Not started: limit of 2 active projects reached", scope.Reason("d"));
        var warning = Assert.Single(scope.Notifications);
        Assert.Equal("2 projects not started", warning.Title);
        Assert.Contains("c, d", warning.Message);
        Assert.Equal(UserNotificationCategory.Warning, warning.Category);
    }

    [Fact]
    public async Task Launch_under_limit_does_not_notify()
    {
        using var scope = new Scope();
        scope.Apply(limit: 5, "a", "b");

        await scope.Orchestrator.StartActiveProjectsAsync(CancellationToken.None);

        Assert.Equal(["a", "b"], scope.Started);
        Assert.Empty(scope.Notifications);
    }

    [Fact]
    public async Task User_start_ignores_limit_and_clears_reason()
    {
        using var scope = new Scope();
        scope.Apply(limit: 1, "a", "b");
        await scope.Orchestrator.StartActiveProjectsAsync(CancellationToken.None);
        Assert.NotNull(scope.Reason("b"));

        await scope.Orchestrator.StartProjectAsync("b", CancellationToken.None);
        await scope.Orchestrator.StartProjectAsync("b", CancellationToken.None);

        Assert.Equal(["a", "b"], scope.Started);
        Assert.Null(scope.Reason("b"));
    }

    [Fact]
    public async Task Stopped_project_frees_its_slot()
    {
        using var scope = new Scope();
        scope.Apply(limit: 1, "a", "b");
        await scope.Orchestrator.StartActiveProjectsAsync(CancellationToken.None);

        scope.Orchestrator.StopProject("a");

        Assert.Empty(scope.Orchestrator.GetStartedProjectIds());
    }

    [Fact]
    public async Task Settings_save_cold_start_over_limit_is_held_back()
    {
        using var scope = new Scope();
        scope.Apply(limit: 1, "a");
        await scope.Orchestrator.StartActiveProjectsAsync(CancellationToken.None);

        scope.Apply(limit: 1, "a", "b");
        await scope.Orchestrator.RemountLocalProjectsWithoutBuildAsync(
            [new LocalProjectRemountPlan("b", LocalRemountKind.MountFresh)],
            CancellationToken.None);

        Assert.Equal(["a"], scope.Started);
        Assert.Equal("Not started: limit of 1 active project reached", scope.Reason("b"));
        Assert.Equal("1 project not started", scope.Notifications.Single().Title);
    }

    private sealed record Notification(string ProjectId, string Title, string Message, UserNotificationCategory Category);

    private sealed class Scope : IDisposable
    {
        private readonly string root;

        public Scope()
        {
            root = Path.Combine(Path.GetTempPath(), "bm-limit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Orchestrator = new ProjectOrchestrator(Path.Combine(root, "logs"), root);
            Orchestrator.SetRuntimeStarter((runtime, _) =>
            {
                lock (Started)
                {
                    Started.Add(runtime.ProjectId);
                }

                return Task.CompletedTask;
            });
            Orchestrator.UserNotification += (projectId, title, message, _, category) =>
                Notifications.Add(new Notification(projectId, title, message, category));
        }

        public ProjectOrchestrator Orchestrator { get; }
        public List<string> Started { get; } = [];
        public List<Notification> Notifications { get; } = [];

        public void Apply(int limit, params string[] ids)
        {
            var settings = new AppSettings
            {
                SchemaVersion = SettingsSchemaV26.Version,
                Monitor = new GlobalMonitorSettings { MaxConcurrentActiveProjects = limit },
                Projects = ids.Select(CreateProject).ToList()
            };
            Orchestrator.ApplySettings(settings);
        }

        public string? Reason(string projectId) =>
            Orchestrator.TryGetRuntimeForTest(projectId)?.NotStartedReason;

        private MonitoredProjectSettings CreateProject(string id)
        {
            var folder = Path.Combine(root, id);
            Directory.CreateDirectory(folder);
            return new MonitoredProjectSettings
            {
                Id = id,
                DisplayName = id,
                IsActiveInSession = true,
                Local = new LocalProjectAttachment
                {
                    RootFolder = folder,
                    ProjectFile = "App.csproj",
                    StartOnLaunch = true,
                    RunOptions = new ProjectRunOptions { RunMode = ProjectRunMode.Watch }
                }
            };
        }

        public void Dispose()
        {
            Orchestrator.Dispose();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup; a watcher may still hold the folder briefly.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above.
            }
        }
    }
}
