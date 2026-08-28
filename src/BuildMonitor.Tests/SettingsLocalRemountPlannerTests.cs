using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;
using BuildMonitor.Infrastructure.Services;

namespace BuildMonitor.Tests;

public sealed class SettingsLocalRemountPlannerTests
{
    [Fact]
    public void Watch_exclude_change_is_watcher_only_for_affected_project()
    {
        var before = TwoProjects();
        var after = Clone(before);
        after.Projects[0].Local!.RunOptions.WatchExcludeSegments = "bin;obj;custom";

        var plans = SettingsLocalRemountPlanner.Plan(before, after);
        Assert.Single(plans);
        Assert.Equal(before.Projects[0].Id, plans[0].ProjectId);
        Assert.Equal(LocalRemountKind.WatcherOnly, plans[0].Kind);
        Assert.DoesNotContain(plans, p => p.ProjectId == before.Projects[1].Id);
    }

    [Fact]
    public void Launch_profile_and_args_and_run_mode_are_process_and_watcher()
    {
        var before = Sample();
        var afterProfile = Clone(before);
        afterProfile.Projects[0].Local!.LaunchProfile = "https";
        Assert.Equal(
            LocalRemountKind.ProcessAndWatcher,
            Assert.Single(SettingsLocalRemountPlanner.Plan(before, afterProfile)).Kind);

        var afterArgs = Clone(before);
        afterArgs.Projects[0].Local!.ExtraDotNetArgs = "--verbosity minimal";
        Assert.Equal(
            LocalRemountKind.ProcessAndWatcher,
            Assert.Single(SettingsLocalRemountPlanner.Plan(before, afterArgs)).Kind);

        var afterMode = Clone(before);
        afterMode.Projects[0].Local!.RunOptions.RunMode = ProjectRunMode.None;
        Assert.Equal(
            LocalRemountKind.ProcessAndWatcher,
            Assert.Single(SettingsLocalRemountPlanner.Plan(before, afterMode)).Kind);
    }

    [Fact]
    public void Root_and_project_file_are_source_identity()
    {
        var before = Sample();
        var afterRoot = Clone(before);
        afterRoot.Projects[0].Local!.RootFolder = @"C:\src\Other";
        Assert.Equal(
            LocalRemountKind.SourceIdentity,
            Assert.Single(SettingsLocalRemountPlanner.Plan(before, afterRoot)).Kind);

        var afterProj = Clone(before);
        afterProj.Projects[0].Local!.ProjectFile = "Other.csproj";
        Assert.Equal(
            LocalRemountKind.SourceIdentity,
            Assert.Single(SettingsLocalRemountPlanner.Plan(before, afterProj)).Kind);
    }

    [Fact]
    public void Active_toggle_stop_and_mount_fresh()
    {
        var before = Sample();
        var afterOff = Clone(before);
        afterOff.Projects[0].IsActiveInSession = false;
        Assert.Equal(
            LocalRemountKind.StopOnly,
            Assert.Single(SettingsLocalRemountPlanner.Plan(before, afterOff)).Kind);

        var afterOn = Clone(afterOff);
        afterOn.Projects[0].IsActiveInSession = true;
        Assert.Equal(
            LocalRemountKind.MountFresh,
            Assert.Single(SettingsLocalRemountPlanner.Plan(afterOff, afterOn)).Kind);
    }

    [Fact]
    public void Hard_plan_lists_only_changed_project()
    {
        var before = TwoProjects();
        var after = Clone(before);
        after.Projects[0].Local!.LaunchProfile = "http";

        var plan = SettingsApplyImpactClassifier.CreatePlan(before, after);
        Assert.True(plan.RemountAffectedLocalProjectsWithoutBuild);
        Assert.False(plan.ColdStartActiveProjectsWithBuild);
        Assert.Single(plan.LocalRemounts);
        Assert.Equal(before.Projects[0].Id, plan.LocalRemounts[0].ProjectId);
    }

    private static AppSettings Sample() => new()
    {
        Projects =
        [
            TestProjectFactory.LocalOnly(
                displayName: "A",
                id: "a",
                rootFolder: @"C:\src\A",
                projectFile: "A.csproj",
                runOptions: new ProjectRunOptions { RunMode = ProjectRunMode.Watch })
        ]
    };

    private static AppSettings TwoProjects()
    {
        var s = Sample();
        s.Projects.Add(TestProjectFactory.LocalOnly(
            displayName: "B",
            id: "b",
            rootFolder: @"C:\src\B",
            projectFile: "B.csproj",
            runOptions: new ProjectRunOptions { RunMode = ProjectRunMode.Watch }));
        return s;
    }

    private static AppSettings Clone(AppSettings settings) =>
        System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(settings))!;
}

public sealed class ProjectRuntimeRemountWithoutBuildTests
{
    [Fact]
    public async Task Watcher_only_remount_creates_watcher_without_build()
    {
        await using var scope = await RemountTestScope.CreateAsync(ProjectRunMode.None, ProjectBuildControlMode.FileWatching);
        var beforeWatchers = scope.Runtime.WatcherCreateCount;

        await scope.Runtime.RemountWithoutBuildAsync(LocalRemountKind.WatcherOnly, CancellationToken.None);

        Assert.Equal(1, scope.Runtime.RemountWithoutBuildCount);
        Assert.Equal(0, scope.Runtime.BuildAsyncInvocationCount);
        Assert.True(scope.Runtime.WatcherCreateCount > beforeWatchers);
    }

    [Fact]
    public async Task Ai_controlled_source_identity_remount_with_start_on_launch_does_not_build()
    {
        await using var scope = await RemountTestScope.CreateAsync(
            ProjectRunMode.Watch,
            ProjectBuildControlMode.AiControlled);
        scope.Runtime.UpdateDefinition(
            TestProjectFactory.LocalOnly(
                id: scope.Runtime.ProjectId,
                rootFolder: scope.Root,
                projectFile: "App.csproj",
                buildControlMode: ProjectBuildControlMode.AiControlled,
                runOptions: new ProjectRunOptions { RunMode = ProjectRunMode.Watch }),
            new GlobalMonitorSettings());
        GetDefinition(scope.Runtime).Local!.StartOnLaunch = true;

        await scope.Runtime.RemountWithoutBuildAsync(LocalRemountKind.SourceIdentity, CancellationToken.None);

        Assert.Equal(1, scope.Runtime.RemountWithoutBuildCount);
        Assert.Equal(0, scope.Runtime.BuildAsyncInvocationCount);
    }

    [Fact]
    public async Task Process_and_watcher_remount_restarts_process_path_without_build()
    {
        await using var scope = await RemountTestScope.CreateAsync(
            ProjectRunMode.Run,
            ProjectBuildControlMode.FileWatching);
        SetPrivate(scope.Runtime, "lastBuildExitCode", 0);

        await scope.Runtime.RemountWithoutBuildAsync(LocalRemountKind.ProcessAndWatcher, CancellationToken.None);

        Assert.Equal(1, scope.Runtime.RemountWithoutBuildCount);
        Assert.Equal(0, scope.Runtime.BuildAsyncInvocationCount);
        Assert.True(scope.Runtime.ProcessStartCount >= 1);
        Assert.True(scope.Runtime.WatcherCreateCount >= 1);
    }

    [Fact]
    public async Task Mount_fresh_does_not_build()
    {
        await using var scope = await RemountTestScope.CreateAsync(
            ProjectRunMode.Watch,
            ProjectBuildControlMode.AiControlled);

        await scope.Runtime.RemountWithoutBuildAsync(LocalRemountKind.MountFresh, CancellationToken.None);

        Assert.Equal(0, scope.Runtime.BuildAsyncInvocationCount);
        Assert.Equal(1, scope.Runtime.RemountWithoutBuildCount);
    }

    private static MonitoredProjectSettings GetDefinition(ProjectRuntime runtime)
    {
        var field = typeof(ProjectRuntime).GetField(
            "projectSettings",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return (MonitoredProjectSettings)field!.GetValue(runtime)!;
    }

    private static void SetPrivate(object target, string name, object value)
    {
        var field = target.GetType().GetField(
            name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field!.SetValue(target, value);
    }

    private sealed class RemountTestScope : IAsyncDisposable
    {
        private RemountTestScope(ProjectRuntime runtime, string root, string logsRoot, string dataRoot)
        {
            Runtime = runtime;
            Root = root;
            this.logsRoot = logsRoot;
            this.dataRoot = dataRoot;
        }

        public ProjectRuntime Runtime { get; }
        public string Root { get; }
        private readonly string logsRoot;
        private readonly string dataRoot;

        public static Task<RemountTestScope> CreateAsync(
            ProjectRunMode runMode,
            ProjectBuildControlMode mode)
        {
            var root = CreateTempDir();
            var logsRoot = CreateTempDir();
            var dataRoot = CreateTempDir();
            var definition = TestProjectFactory.LocalOnly(
                displayName: "RemountProbe",
                rootFolder: root,
                projectFile: "App.csproj",
                buildControlMode: mode,
                runOptions: new ProjectRunOptions
                {
                    RunMode = runMode,
                    FileChanges = FileChangeMode.TriggerRebuild
                });
            definition.Local!.StartOnLaunch = true;

            var runtime = new ProjectRuntime(
                definition,
                new BuildLogStore(logsRoot),
                new DotNetCliRunner(),
                new BuildTriggerJournal(dataRoot),
                new FileChangeBurstStatsStore(dataRoot),
                new BuildTrainingStore(dataRoot));

            return Task.FromResult(new RemountTestScope(runtime, root, logsRoot, dataRoot));
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Runtime.StopAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }

            Runtime.Dispose();
            TryDelete(Root);
            TryDelete(logsRoot);
            TryDelete(dataRoot);
        }

        private static string CreateTempDir()
        {
            var path = Path.Combine(Path.GetTempPath(), "bm-remount-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TryDelete(string path)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }
}
