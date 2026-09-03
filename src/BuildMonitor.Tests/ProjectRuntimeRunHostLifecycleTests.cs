using System.Reflection;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.ControlPlane;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;
using BuildMonitor.Infrastructure.Services;

namespace BuildMonitor.Tests;

/// <summary>
/// Desired run-host state vs temporary operational pause (#106).
/// </summary>
public sealed class ProjectRuntimeRunHostLifecycleTests
{
    [Fact]
    public void Cold_start_StartOnLaunch_sets_desired_Running_without_calling_StartAsync_build()
    {
        // StartAsync sets Desired=Running at entry before freshness work.
        // Simulate the cold-start intent without a real build by mirroring that assignment
        // then verifying EnsureRunProcessStartedAfterBuild may start when output is current.
        using var env = CreateRuntime(ProjectRunMode.Run, startOnLaunch: true);
        SetPrivate(env.Runtime, "desiredRunHostState", DesiredRunHostState.Running);
        SetPrivate(env.Runtime, "lastBuildExitCode", 0);

        var before = env.Runtime.ProcessStartCount;
        env.Runtime.EnsureRunProcessStartedAfterBuild();

        Assert.Equal(DesiredRunHostState.Running, env.Runtime.DesiredRunHostState);
        Assert.True(env.Runtime.ProcessStartCount > before);
    }

    [Fact]
    public void Cold_start_with_stale_output_requires_build_before_host_start()
    {
        using var env = CreateRuntime(ProjectRunMode.Run, startOnLaunch: true);
        SetPrivate(env.Runtime, "desiredRunHostState", DesiredRunHostState.Running);
        SetPrivate(env.Runtime, "lastBuildExitCode", 1);

        var before = env.Runtime.ProcessStartCount;
        env.Runtime.EnsureRunProcessStartedAfterBuild();

        Assert.Equal(DesiredRunHostState.Running, env.Runtime.DesiredRunHostState);
        Assert.Equal(before, env.Runtime.ProcessStartCount);
    }

    [Fact]
    public void Cold_start_StartOnLaunch_false_leaves_host_stopped()
    {
        using var env = CreateRuntime(ProjectRunMode.Run, startOnLaunch: false);

        Assert.Equal(DesiredRunHostState.Stopped, env.Runtime.DesiredRunHostState);
        Assert.Equal(0, env.Runtime.ProcessStartCount);

        env.Runtime.EnsureRunProcessStartedAfterBuild();
        Assert.Equal(0, env.Runtime.ProcessStartCount);
        Assert.Equal(ControlPlaneWatchState.Stopped, env.Runtime.GetWatchStatus().Watch);
    }

    [Fact]
    public async Task Explicit_stop_then_ship_check_resume_path_does_not_start_host()
    {
        using var env = CreateRuntime(ProjectRunMode.Run, startOnLaunch: true);
        SetPrivate(env.Runtime, "desiredRunHostState", DesiredRunHostState.Running);
        SetPrivate(env.Runtime, "lastBuildExitCode", 0);
        env.Runtime.EnsureRunProcessStartedAfterBuild();
        var started = env.Runtime.ProcessStartCount;
        Assert.True(started >= 1);

        await env.Runtime.StopRunAsync(CancellationToken.None);
        Assert.Equal(DesiredRunHostState.Stopped, env.Runtime.DesiredRunHostState);
        Assert.Equal(ControlPlaneWatchState.Stopped, env.Runtime.GetWatchStatus().Watch);

        // Mirror ship-check finally: PauseWatch + conditional ResumeWatch.
        await env.Runtime.PauseWatchAsync(CancellationToken.None);
        if (RunHostLifecyclePolicy.ShouldResumeHostAfterOperation(
                env.Runtime.DesiredRunHostState,
                ProjectRunMode.Run))
        {
            env.Runtime.ResumeWatch();
        }

        Assert.Equal(started, env.Runtime.ProcessStartCount);
        Assert.Equal(DesiredRunHostState.Stopped, env.Runtime.DesiredRunHostState);
    }

    [Fact]
    public async Task Explicit_stop_then_agent_rebuild_ensure_path_does_not_start_host()
    {
        using var env = CreateRuntime(ProjectRunMode.Run, startOnLaunch: true);
        SetPrivate(env.Runtime, "desiredRunHostState", DesiredRunHostState.Running);
        SetPrivate(env.Runtime, "lastBuildExitCode", 0);
        env.Runtime.EnsureRunProcessStartedAfterBuild();
        var started = env.Runtime.ProcessStartCount;

        await env.Runtime.StopRunAsync(CancellationToken.None);

        // Agent rebuild mid-flow EnsureRunProcessStartedAfterBuild + ResumeWatch.
        env.Runtime.EnsureRunProcessStartedAfterBuild();
        env.Runtime.ResumeWatch();

        Assert.Equal(started, env.Runtime.ProcessStartCount);
        Assert.Equal(DesiredRunHostState.Stopped, env.Runtime.DesiredRunHostState);
    }

    [Fact]
    public async Task Explicit_stop_then_test_restart_path_does_not_start_host()
    {
        using var env = CreateRuntime(ProjectRunMode.Run, startOnLaunch: true);
        SetPrivate(env.Runtime, "desiredRunHostState", DesiredRunHostState.Running);
        SetPrivate(env.Runtime, "lastBuildExitCode", 0);
        env.Runtime.EnsureRunProcessStartedAfterBuild();
        var started = env.Runtime.ProcessStartCount;

        await env.Runtime.StopRunAsync(CancellationToken.None);

        var restart = typeof(ProjectRuntime).GetMethod(
            "RestartRunProcessAfterTestsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(restart);
        await (Task)restart!.Invoke(env.Runtime, null)!;

        Assert.Equal(started, env.Runtime.ProcessStartCount);
    }

    [Fact]
    public async Task Intentional_stop_is_not_classified_as_crash()
    {
        using var env = CreateRuntime(ProjectRunMode.Run, startOnLaunch: true);
        SetPrivate(env.Runtime, "desiredRunHostState", DesiredRunHostState.Running);
        SetPrivate(env.Runtime, "lastBuildExitCode", 0);
        env.Runtime.EnsureRunProcessStartedAfterBuild();

        await env.Runtime.StopRunAsync(CancellationToken.None);

        Assert.Equal(0, GetPrivate<int>(env.Runtime, "restartCount"));
        Assert.NotEqual(ProjectLifecycleState.Crashed, env.Runtime.Snapshot.State);
        Assert.Equal(DesiredRunHostState.Stopped, env.Runtime.DesiredRunHostState);
    }

    [Fact]
    public void Unexpected_nonzero_exit_with_desired_Running_and_RestartOnCrash_restarts()
    {
        using var env = CreateRuntime(
            ProjectRunMode.Run,
            startOnLaunch: true,
            restartOnCrash: true,
            maxRetries: 3);
        SetPrivate(env.Runtime, "desiredRunHostState", DesiredRunHostState.Running);
        SetPrivate(env.Runtime, "lastBuildExitCode", 0);
        env.Runtime.EnsureRunProcessStartedAfterBuild();
        var afterStart = env.Runtime.ProcessStartCount;

        // Attach a synthetic process reference so OnRunProcessExited has something to read.
        var supervised = new SupervisedProcess(env.Runtime.ProjectId);
        SetPrivate(env.Runtime, "runProcess", supervised);
        InvokeOnRunProcessExited(env.Runtime, exitCode: 1);

        Assert.True(env.Runtime.ProcessStartCount > afterStart);
        Assert.Equal(1, GetPrivate<int>(env.Runtime, "restartCount"));
        Assert.Equal(DesiredRunHostState.Running, env.Runtime.DesiredRunHostState);
    }

    [Fact]
    public void Unexpected_nonzero_exit_with_RestartOnCrash_false_remains_crashed()
    {
        using var env = CreateRuntime(
            ProjectRunMode.Run,
            startOnLaunch: true,
            restartOnCrash: false);
        SetPrivate(env.Runtime, "desiredRunHostState", DesiredRunHostState.Running);
        SetPrivate(env.Runtime, "lastBuildExitCode", 0);
        env.Runtime.EnsureRunProcessStartedAfterBuild();
        var afterStart = env.Runtime.ProcessStartCount;

        var supervised = new SupervisedProcess(env.Runtime.ProjectId);
        SetPrivate(env.Runtime, "runProcess", supervised);
        InvokeOnRunProcessExited(env.Runtime, exitCode: 1);

        Assert.Equal(afterStart, env.Runtime.ProcessStartCount);
        Assert.Equal(ProjectLifecycleState.Crashed, env.Runtime.Snapshot.State);
        Assert.Equal(0, GetPrivate<int>(env.Runtime, "restartCount"));
    }

    [Fact]
    public async Task Explicit_restart_after_stop_sets_desired_Running_and_starts_once()
    {
        using var env = CreateRuntime(ProjectRunMode.Run, startOnLaunch: true);
        SetPrivate(env.Runtime, "desiredRunHostState", DesiredRunHostState.Running);
        SetPrivate(env.Runtime, "lastBuildExitCode", 0);
        env.Runtime.EnsureRunProcessStartedAfterBuild();

        await env.Runtime.StopRunAsync(CancellationToken.None);
        var afterStop = env.Runtime.ProcessStartCount;

        await env.Runtime.RestartAppAsync(CancellationToken.None);

        Assert.Equal(DesiredRunHostState.Running, env.Runtime.DesiredRunHostState);
        Assert.Equal(afterStop + 1, env.Runtime.ProcessStartCount);
    }

    [Fact]
    public async Task Ai_controlled_file_changes_after_explicit_stop_do_not_start_host()
    {
        using var env = CreateRuntime(
            ProjectRunMode.Run,
            startOnLaunch: true,
            buildControlMode: ProjectBuildControlMode.AiControlled);
        var sessionStore = new ControlPlaneSessionStore();
        env.Runtime.SetSessionStore(sessionStore);
        env.Runtime.SetBuildControlMode(ProjectBuildControlMode.AiControlled);

        SetPrivate(env.Runtime, "desiredRunHostState", DesiredRunHostState.Running);
        SetPrivate(env.Runtime, "lastBuildExitCode", 0);
        env.Runtime.EnsureRunProcessStartedAfterBuild();
        var started = env.Runtime.ProcessStartCount;

        await env.Runtime.StopRunAsync(CancellationToken.None);

        InvokePrivate(
            env.Runtime,
            "OnFileWatcherChanged",
            new object[] { new[] { Path.Combine(env.LogsRoot, "Changed.cs") }, 0 });
        Thread.Sleep(50);

        Assert.Equal(DesiredRunHostState.Stopped, env.Runtime.DesiredRunHostState);
        Assert.Equal(started, env.Runtime.ProcessStartCount);
        Assert.Equal(0, GetPrivate<int>(env.Runtime, "buildInProgress"));
    }

    [Fact]
    public async Task Operational_pause_keeps_desired_Running_and_resume_restores_host()
    {
        using var env = CreateRuntime(ProjectRunMode.Run, startOnLaunch: true);
        SetPrivate(env.Runtime, "desiredRunHostState", DesiredRunHostState.Running);
        SetPrivate(env.Runtime, "lastBuildExitCode", 0);
        env.Runtime.EnsureRunProcessStartedAfterBuild();
        var started = env.Runtime.ProcessStartCount;

        await env.Runtime.PauseWatchAsync(CancellationToken.None);
        Assert.Equal(DesiredRunHostState.Running, env.Runtime.DesiredRunHostState);
        Assert.Equal(ControlPlaneWatchState.Paused, env.Runtime.GetWatchStatus().Watch);

        env.Runtime.ResumeWatch();
        Assert.Equal(DesiredRunHostState.Running, env.Runtime.DesiredRunHostState);
        Assert.True(env.Runtime.ProcessStartCount > started);
    }

    private static RuntimeEnvironment CreateRuntime(
        ProjectRunMode runMode,
        bool startOnLaunch,
        bool restartOnCrash = true,
        int maxRetries = 5,
        ProjectBuildControlMode buildControlMode = ProjectBuildControlMode.FileWatching)
    {
        var logsRoot = CreateTempDir();
        var dataRoot = CreateTempDir();
        var definition = TestProjectFactory.LocalOnly(
            displayName: "Lifecycle",
            id: "lifecycle-" + Guid.NewGuid().ToString("N")[..8],
            rootFolder: logsRoot,
            projectFile: Path.Combine(logsRoot, "App.csproj"),
            buildControlMode: buildControlMode,
            runOptions: new ProjectRunOptions
            {
                RunMode = runMode,
                RestartOnCrash = restartOnCrash,
                MaxRestartRetries = maxRetries,
                RestartAppAfterRebuild = true,
                FileChanges = FileChangeMode.TriggerRebuild
            });
        definition.Local!.StartOnLaunch = startOnLaunch;

        var runtime = new ProjectRuntime(
            definition,
            new BuildLogStore(logsRoot),
            new DotNetCliRunner(),
            new BuildTriggerJournal(dataRoot),
            new FileChangeBurstStatsStore(dataRoot),
            new BuildTrainingStore(dataRoot));

        return new RuntimeEnvironment(logsRoot, dataRoot, runtime);
    }

    private sealed class RuntimeEnvironment : IDisposable
    {
        public RuntimeEnvironment(string logsRoot, string dataRoot, ProjectRuntime runtime)
        {
            LogsRoot = logsRoot;
            DataRoot = dataRoot;
            Runtime = runtime;
        }

        public string LogsRoot { get; }
        public string DataRoot { get; }
        public ProjectRuntime Runtime { get; }

        public void Dispose()
        {
            try
            {
                Runtime.StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // ignore
            }

            Runtime.Dispose();
            TryDelete(LogsRoot);
            TryDelete(DataRoot);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "bm-runhost-" + Guid.NewGuid().ToString("N"));
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

    private static void SetPrivate(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static T GetPrivate<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field!.GetValue(target)!;
    }

    private static void InvokeOnRunProcessExited(ProjectRuntime runtime, int exitCode)
    {
        var method = typeof(ProjectRuntime).GetMethod(
            "OnRunProcessExited",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(runtime, [exitCode]);
    }

    private static void InvokePrivate(object target, string methodName, object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(target, args);
    }
}
