using System.Reflection;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.ControlPlane;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;
using BuildMonitor.Infrastructure.Services;

namespace BuildMonitor.Tests;

public sealed class ControlPlaneHealthVisibilityTests
{
    [Fact]
    public void BuildSnapshot_reports_busy_and_held_auto_build_after_session_busy()
    {
        using var env = CreateRuntimeEnvironment();
        var sessionStore = new ControlPlaneSessionStore();
        env.Runtime.SetSessionStore(sessionStore);

        sessionStore.MarkBusy(env.ProjectId);

        var snapshot = env.Runtime.BuildSnapshot();
        var controlPlane = snapshot.ControlPlane!;

        Assert.True(controlPlane.SessionApiUsed);
        Assert.Equal(ControlPlaneSessionState.Busy, controlPlane.EffectiveSessionState);
        Assert.True(controlPlane.AutoBuildBlockedBySession);

        var card = StatusPanelPresentationBuilder.Build([snapshot], panelDismissAtUtc: null, DateTimeOffset.UtcNow)
            .Cards[0];
        var agent = Assert.Single(card.StatusRows, r => r.Label == "AGENT");
        Assert.Equal("Busy", agent.Primary);
        Assert.Contains("Builds paused", agent.Secondary);
    }

    [Fact]
    public void File_change_while_busy_queues_rebuild_without_starting_build()
    {
        using var env = CreateRuntimeEnvironment();
        var sessionStore = new ControlPlaneSessionStore();
        env.Runtime.SetSessionStore(sessionStore);
        sessionStore.MarkBusy(env.ProjectId);

        InvokePrivateMethod(
            env.Runtime,
            "OnFileWatcherChanged",
            new object[] { new[] { Path.Combine(env.LogsRoot, "Program.cs") }, 0 });

        var snapshot = env.Runtime.BuildSnapshot();

        Assert.Equal(0, GetPrivateField<int>(env.Runtime, "buildInProgress"));
        Assert.True(snapshot.ControlPlane!.HasPendingFileChangeRebuild);
        Assert.True(snapshot.ControlPlane.PendingFileChangeCount >= 1);
        Assert.True(snapshot.ControlPlane.AutoBuildBlockedBySession);
    }

    [Fact]
    public void MarkIdle_updates_snapshot_to_connected_idle()
    {
        using var env = CreateRuntimeEnvironment();
        var sessionStore = new ControlPlaneSessionStore();
        env.Runtime.SetSessionStore(sessionStore);
        sessionStore.MarkBusy(env.ProjectId);
        sessionStore.MarkIdle(env.ProjectId);

        var snapshot = env.Runtime.BuildSnapshot();

        Assert.Equal(ControlPlaneSessionState.Idle, snapshot.ControlPlane!.EffectiveSessionState);
        Assert.False(snapshot.ControlPlane.AutoBuildBlockedBySession);

        var presentation = ControlPlaneStatusFormatter.Format(snapshot, DateTimeOffset.UtcNow);
        Assert.Equal("Idle", presentation.AgentPrimary);
        Assert.Equal("Build allowed", presentation.AgentSecondary);
    }

    [Fact]
    public void Busy_timeout_clears_auto_build_block_and_snapshot_reflects_idle()
    {
        var sessionStore = new ControlPlaneSessionStore();
        sessionStore.ApplyMonitorDefaults(busyTimeoutSeconds: 30, suppressAutoBuildTestsDefault: true);
        sessionStore.MarkBusy("demo");

        var expiredAt = DateTimeOffset.UtcNow.AddSeconds(31);
        var status = sessionStore.GetStatus("demo", expiredAt);

        Assert.Equal(ControlPlaneSessionState.Idle, status.State);
        Assert.False(ControlPlaneSessionPolicy.ShouldBlockAutoBuild(status.SessionApiUsed, status.State));
    }

    [Fact]
    public void NotifyControlPlaneChanged_marks_health_dirty_for_immediate_coalesce()
    {
        using var env = CreateRuntimeEnvironment();
        env.Runtime.NotifyControlPlaneChanged(immediate: true);

        Assert.True(env.Runtime.TryCoalesceHealth());
    }

    [Fact]
    public void Ship_check_in_progress_is_visible_in_snapshot_and_card()
    {
        using var env = CreateRuntimeEnvironment();
        var sessionStore = new ControlPlaneSessionStore();
        env.Runtime.SetSessionStore(sessionStore);
        sessionStore.MarkBusy(env.ProjectId);
        sessionStore.MarkIdle(env.ProjectId);

        SetPrivateField(env.Runtime, "shipCheckInProgress", 1);
        SetPrivateField(env.Runtime, "shipCheckPhase", ControlPlaneShipCheckPhase.Testing);

        var snapshot = env.Runtime.BuildSnapshot();

        Assert.True(snapshot.ControlPlane!.ShipCheckInProgress);
        Assert.Equal(ControlPlaneShipCheckPhase.Testing, snapshot.ControlPlane.ShipCheckPhase);

        var card = StatusPanelPresentationBuilder.Build([snapshot], null, DateTimeOffset.UtcNow).Cards[0];
        var build = Assert.Single(card.StatusRows, r => r.Label == "BUILD");
        Assert.Equal("Ship check · Testing", build.Primary);
    }

    [Fact]
    public void Failed_build_stays_red_when_agent_is_idle()
    {
        var controlPlane = new ProjectControlPlaneSnapshot(
            SessionApiUsed: true,
            EffectiveSessionState: ControlPlaneSessionState.Idle,
            SessionSinceUtc: DateTimeOffset.UtcNow,
            AutoBuildBlockedBySession: false,
            HasPendingFileChangeRebuild: false,
            PendingFileChangeCount: 0,
            ShipCheckPhase: ControlPlaneShipCheckPhase.None,
            LastShipCheckOutcome: ControlPlaneShipCheckOutcome.None,
            LastShipCheckCompletedUtc: null,
            ShipCheckInProgress: false);

        var snapshot = new ProjectHealthSnapshot(
            ProjectId: "demo",
            DisplayName: "Demo",
            Health: MonitorHealth.Red,
            HealthLabel: "Failed",
            State: ProjectLifecycleState.Watching,
            LastExitCode: 1,
            LastDuration: TimeSpan.FromSeconds(8),
            LastErrorPreview: "error CS0001",
            ErrorCount: 1,
            WarningCount: 0,
            LastChangedUtc: DateTimeOffset.UtcNow,
            LastBuildFinishedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            IsActive: true,
            ProgressSteps: [],
            LastBuildExitCode: 1,
            ControlPlane: controlPlane);

        Assert.Equal(MonitorHealth.Red, snapshot.Health);
        Assert.Equal("Idle", ControlPlaneStatusFormatter.Format(snapshot, DateTimeOffset.UtcNow).AgentPrimary);
    }

    [Fact]
    public void StatusPanelCardUiChangeDetector_detects_control_plane_transitions()
    {
        var before = CreateHealthSnapshot(
            ControlPlaneSessionState.Busy,
            pendingRebuild: false,
            pendingCount: 0);
        var after = CreateHealthSnapshot(
            ControlPlaneSessionState.Idle,
            pendingRebuild: true,
            pendingCount: 2);

        Assert.True(StatusPanelCardUiChangeDetector.RequiresCardRebuild([before], [after]));
    }

    [Fact]
    public void StatusPanelPresentationChangeDetector_treats_control_plane_headline_as_urgent()
    {
        var previous = StatusPanelPresentationBuilder.Build(
            [CreateHealthSnapshot(ControlPlaneSessionState.Busy, false, 0)],
            null,
            DateTimeOffset.UtcNow);
        var current = StatusPanelPresentationBuilder.Build(
            [CreateHealthSnapshot(ControlPlaneSessionState.Idle, false, 0)],
            null,
            DateTimeOffset.UtcNow);

        Assert.True(StatusPanelPresentationChangeDetector.RequiresUrgentCardRebuild(previous, current));
    }

    [Fact]
    public void Ai_controlled_file_changes_observe_but_never_start_build()
    {
        using var env = CreateRuntimeEnvironment();
        var sessionStore = new ControlPlaneSessionStore();
        env.Runtime.SetSessionStore(sessionStore);

        env.Runtime.SetBuildControlMode(ProjectBuildControlMode.AiControlled);
        sessionStore.MarkBusy(env.ProjectId);

        InvokePrivateMethod(
            env.Runtime,
            "OnFileWatcherChanged",
            new object[] { new[] { Path.Combine(env.LogsRoot, "A.cs") }, 0 });
        InvokePrivateMethod(
            env.Runtime,
            "OnFileWatcherChanged",
            new object[] { new[] { Path.Combine(env.LogsRoot, "B.cs") }, 0 });

        sessionStore.MarkIdle(env.ProjectId);

        InvokePrivateMethod(
            env.Runtime,
            "OnFileWatcherChanged",
            new object[] { new[] { Path.Combine(env.LogsRoot, "C.cs") }, 0 });

        Thread.Sleep(50);

        var snapshot = env.Runtime.BuildSnapshot();
        Assert.Equal(0, GetPrivateField<int>(env.Runtime, "buildInProgress"));
        Assert.Equal(ProjectBuildControlMode.AiControlled, snapshot.ControlPlane!.BuildControlMode);
        Assert.False(snapshot.ControlPlane.AutoBuildEnabled);
        Assert.True(snapshot.ControlPlane.HasPendingFileChangeRebuild);
        Assert.True(snapshot.ControlPlane.PendingFileChangeCount >= 1);
        Assert.Null(snapshot.RebuildQuietUntilUtc);
        Assert.False(snapshot.IsEditGatingActive);
        Assert.NotEqual(ProjectLifecycleState.WaitingForEdits, snapshot.State);
    }

    [Fact]
    public void Ai_controlled_disables_dotnet_watch_even_when_coalesce_off()
    {
        using var env = CreateRuntimeEnvironment();
        var definition = TestProjectFactory.LocalOnly(
            displayName: "Demo",
            id: env.ProjectId,
            rootFolder: env.LogsRoot,
            projectFile: Path.Combine(env.LogsRoot, "Demo.csproj"),
            buildControlMode: ProjectBuildControlMode.FileWatching,
            runOptions: new ProjectRunOptions { RunMode = ProjectRunMode.Watch });
        env.Runtime.UpdateDefinition(definition, new GlobalMonitorSettings { CoalesceWatchRebuilds = false });

        Assert.True(InvokePrivateBool(env.Runtime, "UsesDotNetWatchProcess"));

        env.Runtime.SetBuildControlMode(ProjectBuildControlMode.AiControlled);

        Assert.False(InvokePrivateBool(env.Runtime, "UsesDotNetWatchProcess"));
        Assert.False(InvokePrivateBool(env.Runtime, "UsesCoalescedWatchRebuilds"));
    }

    [Fact]
    public void Ai_controlled_idle_after_debounce_window_still_zero_builds()
    {
        using var env = CreateRuntimeEnvironment();
        var sessionStore = new ControlPlaneSessionStore();
        env.Runtime.SetSessionStore(sessionStore);
        env.Runtime.UpdateDefinition(
            TestProjectFactory.LocalOnly(
                displayName: "Demo",
                id: env.ProjectId,
                rootFolder: env.LogsRoot,
                projectFile: Path.Combine(env.LogsRoot, "Demo.csproj"),
                buildControlMode: ProjectBuildControlMode.FileWatching,
                runOptions: new ProjectRunOptions { RunMode = ProjectRunMode.Watch }),
            new GlobalMonitorSettings
            {
                CoalesceWatchRebuilds = false,
                FileChangeDebounceMs = 200
            });

        env.Runtime.SetBuildControlMode(ProjectBuildControlMode.AiControlled);
        sessionStore.MarkBusy(env.ProjectId);

        for (var i = 0; i < 4; i++)
        {
            InvokePrivateMethod(
                env.Runtime,
                "OnFileWatcherChanged",
                new object[] { new[] { Path.Combine(env.LogsRoot, $"F{i}.cs") }, 0 });
        }

        sessionStore.MarkIdle(env.ProjectId);
        Thread.Sleep(600);

        Assert.Equal(0, GetPrivateField<int>(env.Runtime, "buildInProgress"));
        Assert.True(env.Runtime.BuildSnapshot().ControlPlane!.HasPendingFileChangeRebuild);
        Assert.Null(env.Runtime.BuildSnapshot().RebuildQuietUntilUtc);
    }

    [Fact]
    public void Switching_to_ai_controlled_cancels_pending_schedule_generation()
    {
        using var env = CreateRuntimeEnvironment();
        var sessionStore = new ControlPlaneSessionStore();
        env.Runtime.SetSessionStore(sessionStore);

        SetPrivateField(env.Runtime, "pendingFileChangeRebuild", true);
        SetPrivateField(env.Runtime, "pendingRebuildHoldFileCount", 4);
        var before = GetPrivateField<int>(env.Runtime, "fileChangeRebuildScheduleGeneration");

        var status = env.Runtime.SetBuildControlMode(ProjectBuildControlMode.AiControlled);

        Assert.Equal(ProjectBuildControlMode.FileWatching, status.PreviousMode);
        Assert.Equal(ProjectBuildControlMode.AiControlled, status.Mode);
        Assert.True(GetPrivateField<bool>(env.Runtime, "pendingFileChangeRebuild"));
        Assert.Equal(4, GetPrivateField<int>(env.Runtime, "pendingRebuildHoldFileCount"));
        Assert.True(GetPrivateField<int>(env.Runtime, "fileChangeRebuildScheduleGeneration") > before);
        Assert.Equal(0, GetPrivateField<int>(env.Runtime, "buildInProgress"));
    }

    [Fact]
    public void Switching_to_file_watching_clears_pending_without_starting_build()
    {
        using var env = CreateRuntimeEnvironment();
        env.Runtime.SetBuildControlMode(ProjectBuildControlMode.AiControlled);
        SetPrivateField(env.Runtime, "pendingFileChangeRebuild", true);
        SetPrivateField(env.Runtime, "pendingRebuildHoldFileCount", 3);

        env.Runtime.SetBuildControlMode(ProjectBuildControlMode.FileWatching);

        Assert.False(GetPrivateField<bool>(env.Runtime, "pendingFileChangeRebuild"));
        Assert.Equal(0, GetPrivateField<int>(env.Runtime, "buildInProgress"));
    }

    private static ProjectHealthSnapshot CreateHealthSnapshot(
        ControlPlaneSessionState sessionState,
        bool pendingRebuild,
        int pendingCount)
    {
        var controlPlane = new ProjectControlPlaneSnapshot(
            SessionApiUsed: true,
            EffectiveSessionState: sessionState,
            SessionSinceUtc: DateTimeOffset.UtcNow,
            AutoBuildBlockedBySession: sessionState == ControlPlaneSessionState.Busy,
            HasPendingFileChangeRebuild: pendingRebuild,
            PendingFileChangeCount: pendingCount,
            ShipCheckPhase: ControlPlaneShipCheckPhase.None,
            LastShipCheckOutcome: ControlPlaneShipCheckOutcome.None,
            LastShipCheckCompletedUtc: null,
            ShipCheckInProgress: false);

        return new ProjectHealthSnapshot(
            ProjectId: "demo",
            DisplayName: "Demo",
            Health: MonitorHealth.Green,
            HealthLabel: "Healthy",
            State: ProjectLifecycleState.Watching,
            LastExitCode: 0,
            LastDuration: TimeSpan.FromSeconds(5),
            LastErrorPreview: null,
            ErrorCount: 0,
            WarningCount: 0,
            LastChangedUtc: DateTimeOffset.UtcNow,
            LastBuildFinishedAtUtc: DateTimeOffset.UtcNow,
            IsActive: true,
            ProgressSteps: [],
            ControlPlane: controlPlane);
    }

    private static RuntimeEnvironment CreateRuntimeEnvironment()
    {
        var logsRoot = CreateTempDir();
        var dataRoot = CreateTempDir();
        var definition = TestProjectFactory.LocalOnly(
            displayName: "Demo",
            id: "demo",
            rootFolder: logsRoot,
            projectFile: Path.Combine(logsRoot, "Demo.csproj"),
            runOptions: new ProjectRunOptions { RunMode = ProjectRunMode.Watch });

        var runtime = new ProjectRuntime(
            definition,
            new BuildLogStore(logsRoot),
            new DotNetCliRunner(),
            new BuildTriggerJournal(dataRoot),
            new FileChangeBurstStatsStore(dataRoot),
            new BuildTrainingStore(dataRoot));

        return new RuntimeEnvironment(logsRoot, dataRoot, definition.Id, runtime);
    }

    private sealed class RuntimeEnvironment : IDisposable
    {
        public RuntimeEnvironment(
            string logsRoot,
            string dataRoot,
            string projectId,
            ProjectRuntime runtime)
        {
            LogsRoot = logsRoot;
            DataRoot = dataRoot;
            ProjectId = projectId;
            Runtime = runtime;
        }

        public string LogsRoot { get; }
        public string DataRoot { get; }
        public string ProjectId { get; }
        public ProjectRuntime Runtime { get; }

        public void Dispose()
        {
            Runtime.Dispose();
            Directory.Delete(LogsRoot, recursive: true);
            Directory.Delete(DataRoot, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "bm-cp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void InvokePrivateMethod(object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(target, args);
    }

    private static bool InvokePrivateBool(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method!.Invoke(target, null));
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field!.GetValue(target)!;
    }
}
