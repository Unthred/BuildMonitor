using System.Reflection;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;
using BuildMonitor.Infrastructure.Services;

namespace BuildMonitor.Tests;

[Collection("OperationalHistoryEmitter.Serial")]
public sealed class OperationalHistoryEmitterTests
{
    [Fact]
    public async Task Explicit_rebuild_correlates_request_build_lifecycle()
    {
        using var env = await CreateBuildableRuntimeAsync(includeTests: false);
        Assert.True(env.Runtime.TryBeginHistoryOperation(
            OperationalEventSource.User, "rebuild", "Rebuild requested", out var opId));
        env.Runtime.PrepareBuild("manual rebuild");
        await env.Runtime.BuildAsync(CancellationToken.None);
        env.Runtime.EndHistoryOperation(opId);

        var chronological = env.History.Chronological()
            .Where(e => e.ProjectId == env.Runtime.ProjectId)
            .ToList();
        var kinds = chronological.Select(e => (e.Kind, e.Outcome, e.Detail?.ActionName)).ToList();

        Assert.Contains(kinds, k => k.Kind == OperationalEventKind.ExplicitAction && k.ActionName == "rebuild");
        Assert.Contains(kinds, k => k.Kind == OperationalEventKind.Build && k.Outcome == OperationalEventOutcome.Started);
        Assert.Contains(kinds, k => k.Kind == OperationalEventKind.Build
            && (k.Outcome == OperationalEventOutcome.Succeeded || k.Outcome == OperationalEventOutcome.Failed));

        var opIds = chronological
            .Where(e => e.Kind is OperationalEventKind.ExplicitAction or OperationalEventKind.Build)
            .Select(e => e.OperationId)
            .Distinct()
            .ToList();
        Assert.Single(opIds);
        Assert.Equal(opId, opIds[0]);
        Assert.Equal(1, chronological.Count(e => e.Kind == OperationalEventKind.Build && e.Outcome == OperationalEventOutcome.Started));
    }

    [Fact]
    public async Task Explicit_tests_correlates_request_test_lifecycle()
    {
        using var env = await CreateBuildableRuntimeAsync(includeTests: true);
        Assert.True(env.Runtime.TryBeginHistoryOperation(
            OperationalEventSource.User, "rebuild", "Rebuild requested", out var rebuildOp));
        env.Runtime.PrepareBuild("seed");
        await env.Runtime.BuildAsync(CancellationToken.None);
        env.Runtime.EndHistoryOperation(rebuildOp);

        Assert.True(env.Runtime.TryBeginHistoryOperation(
            OperationalEventSource.User, "tests", "Tests requested", out var testsOp));
        env.Runtime.PrepareTest("manual");
        await env.Runtime.TestAsync(CancellationToken.None);
        env.Runtime.EndHistoryOperation(testsOp);

        var chronological = env.History.Chronological()
            .Where(e => e.ProjectId == env.Runtime.ProjectId)
            .ToList();
        var correlated = chronological.Where(e => e.OperationId == testsOp).ToList();

        Assert.Contains(correlated, e => e.Kind == OperationalEventKind.ExplicitAction);
        Assert.Contains(correlated, e => e.Kind == OperationalEventKind.Tests && e.Outcome == OperationalEventOutcome.Started);
        Assert.Contains(correlated, e => e.Kind == OperationalEventKind.Tests
            && (e.Outcome == OperationalEventOutcome.Succeeded || e.Outcome == OperationalEventOutcome.Failed));
        Assert.Equal(1, correlated.Count(e => e.Kind == OperationalEventKind.Tests && e.Outcome == OperationalEventOutcome.Started));
    }

    [Fact]
    public async Task Ship_check_correlates_explicit_action_with_build_lifecycle()
    {
        using var env = await CreateBuildableRuntimeAsync(includeTests: false);
        var result = await env.Runtime.RunShipCheckAsync(null, null, CancellationToken.None);

        var chronological = env.History.Chronological()
            .Where(e => e.ProjectId == env.Runtime.ProjectId)
            .ToList();
        var shipStarted = chronological.First(e =>
            e.Kind == OperationalEventKind.ExplicitAction
            && e.Detail?.ActionName == "ship-check"
            && e.Outcome == OperationalEventOutcome.Started);
        var correlated = chronological.Where(e => e.OperationId == shipStarted.OperationId).ToList();

        Assert.Equal(1, correlated.Count(e => e.Kind == OperationalEventKind.Build && e.Outcome == OperationalEventOutcome.Started));
        Assert.Equal(1, correlated.Count(e => e.Kind == OperationalEventKind.Build
            && e.Outcome is OperationalEventOutcome.Succeeded or OperationalEventOutcome.Failed));
        Assert.Equal(1, correlated.Count(e =>
            e.Kind == OperationalEventKind.ExplicitAction
            && e.Detail?.ActionName == "ship-check"
            && e.Outcome is OperationalEventOutcome.Succeeded or OperationalEventOutcome.Failed));
        Assert.True(correlated.Count(e => e.Kind == OperationalEventKind.Tests && e.Outcome == OperationalEventOutcome.Started) <= 1);
        _ = result;
    }

    [Fact]
    public void Ship_check_style_sequence_shares_one_operation_without_duplicate_edges()
    {
        var store = new FakeOperationalHistoryStore();
        var emitter = new OperationalHistoryEmitter(store, () => "p1");
        Assert.True(emitter.TryBeginCallerOwnedOperation(
            OperationalEventSource.Agent, "ship-check", "Ship-check requested", out var shipOp));
        emitter.RecordBuild(OperationalEventOutcome.Started, "Build started", "trig", 1, null, null);
        emitter.RecordBuild(OperationalEventOutcome.Succeeded, "Build succeeded", "trig", 1, null, null);
        emitter.RecordTests(OperationalEventOutcome.Started, "Tests started", null);
        emitter.RecordTests(OperationalEventOutcome.Succeeded, "Tests succeeded", null);
        emitter.RecordExplicit(
            OperationalEventSource.Agent,
            "ship-check",
            "Ship-check completed",
            OperationalEventOutcome.Succeeded);
        emitter.ClearCallerOwnedOperation(shipOp);

        var chronological = store.Chronological();
        Assert.Single(chronological.Select(e => e.OperationId).Distinct());
        Assert.Equal(1, chronological.Count(e => e.Kind == OperationalEventKind.Build && e.Outcome == OperationalEventOutcome.Started));
        Assert.Equal(1, chronological.Count(e => e.Kind == OperationalEventKind.Tests && e.Outcome == OperationalEventOutcome.Started));
        Assert.Equal(2, chronological.Count(e => e.Detail?.ActionName == "ship-check"));
    }

    [Fact]
    public async Task Overlapping_rebuild_begin_is_rejected_and_first_operation_id_is_preserved()
    {
        using var env = await CreateBuildableRuntimeAsync(includeTests: false);
        Assert.True(env.Runtime.TryBeginHistoryOperation(
            OperationalEventSource.User, "rebuild", "Rebuild requested", out var firstOp));
        env.Runtime.PrepareBuild("manual rebuild");
        var buildTask = env.Runtime.BuildAsync(CancellationToken.None);

        Assert.True(await WaitUntilAsync(() => env.Runtime.IsBuildInProgress, TimeSpan.FromSeconds(10)));

        Assert.False(env.Runtime.TryBeginHistoryOperation(
            OperationalEventSource.User, "rebuild", "Rebuild requested (overlap)", out var refusedOp));
        Assert.Equal(firstOp, refusedOp);
        Assert.Equal(firstOp, env.Runtime.ActiveHistoryOperationId);

        // Rejected overlap must not clear the in-flight operation (null / foreign id are no-ops).
        env.Runtime.EndHistoryOperation(null);
        env.Runtime.EndHistoryOperation("foreign-operation-id");
        Assert.Equal(firstOp, env.Runtime.ActiveHistoryOperationId);

        await buildTask;
        env.Runtime.EndHistoryOperation(firstOp);
        Assert.Null(env.Runtime.ActiveHistoryOperationId);

        var correlated = env.History.Chronological()
            .Where(e => e.ProjectId == env.Runtime.ProjectId && e.OperationId == firstOp)
            .ToList();
        Assert.Contains(correlated, e => e.Kind == OperationalEventKind.ExplicitAction && e.Detail?.ActionName == "rebuild");
        Assert.Contains(correlated, e => e.Kind == OperationalEventKind.Build && e.Outcome == OperationalEventOutcome.Started);
        Assert.Contains(correlated, e => e.Kind == OperationalEventKind.Build
            && e.Outcome is OperationalEventOutcome.Succeeded or OperationalEventOutcome.Failed);
        Assert.Equal(1, env.History.Chronological().Count(e =>
            e.Kind == OperationalEventKind.ExplicitAction && e.Detail?.ActionName == "rebuild"));
    }

    [Fact]
    public async Task Agent_tests_while_rebuild_in_progress_are_rejected_without_stealing_operation_id()
    {
        using var env = await CreateBuildableRuntimeAsync(includeTests: false);
        Assert.True(env.Runtime.TryBeginHistoryOperation(
            OperationalEventSource.User, "rebuild", "Rebuild requested", out var rebuildOp));
        env.Runtime.PrepareBuild("manual rebuild");
        var buildTask = env.Runtime.BuildAsync(CancellationToken.None);
        Assert.True(await WaitUntilAsync(() => env.Runtime.IsBuildInProgress, TimeSpan.FromSeconds(10)));

        var ex = await Record.ExceptionAsync(() =>
            env.Runtime.RunAgentTestsAsync(null, null, CancellationToken.None));
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Build already running", ex!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(rebuildOp, env.Runtime.ActiveHistoryOperationId);

        await buildTask;
        env.Runtime.EndHistoryOperation(rebuildOp);

        Assert.DoesNotContain(
            env.History.Chronological(),
            e => e.Kind == OperationalEventKind.ExplicitAction && e.Detail?.ActionName == "tests");
        Assert.All(
            env.History.Chronological().Where(e => e.Kind == OperationalEventKind.Build),
            e => Assert.Equal(rebuildOp, e.OperationId));
    }

    [Fact]
    public void Mismatched_end_does_not_clear_active_caller_operation()
    {
        var store = new FakeOperationalHistoryStore();
        var emitter = new OperationalHistoryEmitter(store, () => "p1");
        Assert.True(emitter.TryBeginCallerOwnedOperation(
            OperationalEventSource.User, "rebuild", "Rebuild requested", out var id));
        emitter.ClearCallerOwnedOperation("not-the-id");
        Assert.Equal(id, emitter.OperationId);
        Assert.False(emitter.TryBeginCallerOwnedOperation(
            OperationalEventSource.Agent, "tests", "Tests requested", out var refused));
        Assert.Equal(id, refused);
        emitter.ClearCallerOwnedOperation(null);
        Assert.Equal(id, emitter.OperationId);
        emitter.ClearCallerOwnedOperation(id);
        Assert.Null(emitter.OperationId);
    }

    [Fact]
    public async Task File_triggered_build_shares_operation_with_build_lifecycle()
    {
        using var env = await CreateBuildableRuntimeAsync(includeTests: false);
        SetPrivate(env.Runtime, "buildTriggeredByFileChange", 1);
        env.Runtime.PrepareBuild("file change");
        await env.Runtime.BuildAsync(CancellationToken.None);

        var chronological = env.History.Chronological()
            .Where(e => e.ProjectId == env.Runtime.ProjectId)
            .ToList();
        Assert.Contains(chronological, e =>
            e.Kind == OperationalEventKind.ExplicitAction && e.Detail?.ActionName == "file-triggered-build");
        Assert.Contains(chronological, e => e.Kind == OperationalEventKind.Build && e.Outcome == OperationalEventOutcome.Started);
        var opIds = chronological
            .Where(e => e.Kind is OperationalEventKind.ExplicitAction or OperationalEventKind.Build)
            .Select(e => e.OperationId)
            .Distinct()
            .ToList();
        Assert.Single(opIds);
    }

    [Fact]
    public void Ai_controlled_file_change_does_not_emit_build_or_test_lifecycle()
    {
        using var env = CreateRuntime(ProjectBuildControlMode.AiControlled);
        var before = env.History.Events.Count;
        InvokePrivate(env.Runtime, "OnFileWatcherChanged",
            (IReadOnlyList<string>)[Path.Combine(env.Root, "Program.cs")],
            10);

        var after = env.History.Chronological().Where(e => e.ProjectId == env.Runtime.ProjectId).ToList();
        Assert.Equal(before, env.History.Events.Count);
        Assert.DoesNotContain(after, e => e.Kind == OperationalEventKind.Build);
        Assert.DoesNotContain(after, e => e.Kind == OperationalEventKind.Tests);
        Assert.False(BuildTriggerPolicy.ShouldAutoBuildFromFileChange(
            ProjectBuildControlMode.AiControlled,
            sessionApiUsed: true,
            ControlPlaneSessionState.Busy));
    }

    [Fact]
    public async Task Host_stop_emits_stopped_not_crash_and_desired_state_unchanged_semantics()
    {
        using var env = CreateRuntime(ProjectBuildControlMode.FileWatching, ProjectRunMode.Run);
        SetPrivate(env.Runtime, "desiredRunHostState", DesiredRunHostState.Running);
        SetPrivate(env.Runtime, "lastBuildExitCode", 0);
        env.Runtime.EnsureRunProcessStartedAfterBuild();
        Assert.True(env.Runtime.ProcessStartCount >= 1);

        Assert.True(env.Runtime.TryBeginHistoryOperation(
            OperationalEventSource.Agent, "run-stop", "Run stop requested", out var opId));
        await env.Runtime.StopRunAsync(CancellationToken.None);
        env.Runtime.EndHistoryOperation(opId);

        Assert.Equal(DesiredRunHostState.Stopped, env.Runtime.DesiredRunHostState);
        var events = env.History.Chronological().Where(e => e.ProjectId == env.Runtime.ProjectId).ToList();
        Assert.Contains(events, e => e.Kind == OperationalEventKind.ExplicitAction && e.Detail?.ActionName == "run-stop");
        Assert.Contains(events, e => e.Kind == OperationalEventKind.RunHost && e.Detail?.ActionName == "host-stopped");
        Assert.DoesNotContain(events, e => e.Detail?.ActionName == "host-crashed");
    }

    [Fact]
    public async Task Intentional_restart_emits_restarted_not_coincidental_stop_start_pair()
    {
        using var env = CreateRuntime(ProjectBuildControlMode.FileWatching, ProjectRunMode.Run);
        SetPrivate(env.Runtime, "desiredRunHostState", DesiredRunHostState.Running);
        SetPrivate(env.Runtime, "lastBuildExitCode", 0);
        env.Runtime.EnsureRunProcessStartedAfterBuild();

        Assert.True(env.Runtime.TryBeginHistoryOperation(
            OperationalEventSource.User, "run-restart", "Run restart requested", out var opId));
        await env.Runtime.RestartAppAsync(CancellationToken.None);
        env.Runtime.EndHistoryOperation(opId);

        var events = env.History.Chronological().Where(e => e.ProjectId == env.Runtime.ProjectId).ToList();
        Assert.Contains(events, e => e.Detail?.ActionName == "host-restarted");
        Assert.DoesNotContain(events, e => e.Detail?.ActionName == "host-stopped");
        Assert.DoesNotContain(events, e =>
            e.Detail?.ActionName == "host-started"
            && e.OperationId == opId);
    }

    [Fact]
    public async Task History_record_failure_does_not_break_build()
    {
        using var env = await CreateBuildableRuntimeAsync(includeTests: false);
        env.History.ThrowOnRecord = true;
        Assert.True(env.Runtime.TryBeginHistoryOperation(
            OperationalEventSource.User, "rebuild", "Rebuild requested", out var opId));
        env.Runtime.PrepareBuild("manual rebuild");
        var ex = await Record.ExceptionAsync(() => env.Runtime.BuildAsync(CancellationToken.None));
        env.Runtime.EndHistoryOperation(opId);
        Assert.Null(ex);
    }

    [Fact]
    public void Failure_event_uses_structured_detail_only()
    {
        var store = new FakeOperationalHistoryStore();
        var emitter = new OperationalHistoryEmitter(store, () => "p1");
        Assert.True(emitter.TryBeginCallerOwnedOperation(
            OperationalEventSource.User, "rebuild", "Rebuild requested", out _));
        emitter.RecordBuild(
            OperationalEventOutcome.Failed,
            "Build failed",
            "trig1",
            3,
            branch: null,
            new OperationalEventDetail(ExitCode: 1, ErrorPreview: "CS0001", LogKind: BuildLogKind.Build));

        var failed = store.Chronological().Single(e => e.Outcome == OperationalEventOutcome.Failed);
        Assert.Equal(1, failed.Detail?.ExitCode);
        Assert.Equal("CS0001", failed.Detail?.ErrorPreview);
        Assert.Equal(BuildLogKind.Build, failed.Detail?.LogKind);
        Assert.Equal("trig1", failed.BuildTriggerId);
        Assert.Equal(3, failed.LocalBuildNumber);
    }

    [Fact]
    public void Emitter_caller_owned_operation_is_not_cleared_by_runtime_clear()
    {
        var store = new FakeOperationalHistoryStore();
        var emitter = new OperationalHistoryEmitter(store, () => "p1");
        Assert.True(emitter.TryBeginCallerOwnedOperation(
            OperationalEventSource.Agent, "ship-check", "Ship-check requested", out var id));
        emitter.ClearRuntimeOwnedOperation();
        Assert.Equal(id, emitter.OperationId);
        emitter.ClearCallerOwnedOperation(id);
        Assert.Null(emitter.OperationId);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return condition();
    }

    private static async Task<RuntimeEnv> CreateBuildableRuntimeAsync(bool includeTests)
    {
        var root = CreateTempDir();
        var logs = CreateTempDir();
        var data = CreateTempDir();
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Program.cs"),
            """
            Console.WriteLine("ok");
            """);

        string? testProject = null;
        if (includeTests)
        {
            testProject = Path.Combine(root, "App.Tests.csproj");
            await File.WriteAllTextAsync(
                testProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <IsPackable>false</IsPackable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
                    <PackageReference Include="xunit" Version="2.9.2" />
                    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
                    <ProjectReference Include="App.csproj" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(root, "SmokeTests.cs"),
                """
                using Xunit;
                public class SmokeTests
                {
                    [Fact]
                    public void Passes() => Assert.True(true);
                }
                """);
        }

        var history = new FakeOperationalHistoryStore();
        var definition = TestProjectFactory.LocalOnly(
            displayName: "HistoryEmit",
            id: "hist-" + Guid.NewGuid().ToString("N")[..8],
            rootFolder: root,
            projectFile: Path.Combine(root, "App.csproj"),
            runOptions: new ProjectRunOptions
            {
                RunMode = ProjectRunMode.None,
                RunTests = TestRunTrigger.Off,
                FileChanges = FileChangeMode.TriggerRebuild
            });
        if (testProject is not null)
        {
            definition.Local!.TestProjectFile = "App.Tests.csproj";
        }

        var runtime = new ProjectRuntime(
            definition,
            new BuildLogStore(logs),
            new DotNetCliRunner(),
            new BuildTriggerJournal(data),
            new FileChangeBurstStatsStore(data),
            new BuildTrainingStore(data),
            notifyUser: null,
            operationalHistory: history);

        return new RuntimeEnv(root, logs, data, runtime, history);
    }

    private static RuntimeEnv CreateRuntime(
        ProjectBuildControlMode mode,
        ProjectRunMode runMode = ProjectRunMode.None)
    {
        var root = CreateTempDir();
        var logs = CreateTempDir();
        var data = CreateTempDir();
        File.WriteAllText(Path.Combine(root, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        var history = new FakeOperationalHistoryStore();
        var definition = TestProjectFactory.LocalOnly(
            displayName: "HistoryHost",
            id: "hist-host-" + Guid.NewGuid().ToString("N")[..8],
            rootFolder: root,
            projectFile: Path.Combine(root, "App.csproj"),
            buildControlMode: mode,
            runOptions: new ProjectRunOptions
            {
                RunMode = runMode,
                RestartAppAfterRebuild = true,
                FileChanges = FileChangeMode.TriggerRebuild
            });
        var runtime = new ProjectRuntime(
            definition,
            new BuildLogStore(logs),
            new DotNetCliRunner(),
            new BuildTriggerJournal(data),
            new FileChangeBurstStatsStore(data),
            new BuildTrainingStore(data),
            notifyUser: null,
            operationalHistory: history);
        return new RuntimeEnv(root, logs, data, runtime, history);
    }

    private sealed class RuntimeEnv : IDisposable
    {
        public RuntimeEnv(string root, string logs, string data, ProjectRuntime runtime, FakeOperationalHistoryStore history)
        {
            Root = root;
            Logs = logs;
            Data = data;
            Runtime = runtime;
            History = history;
        }

        public string Root { get; }
        public string Logs { get; }
        public string Data { get; }
        public ProjectRuntime Runtime { get; }
        public FakeOperationalHistoryStore History { get; }

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
            TryDelete(Root);
            TryDelete(Logs);
            TryDelete(Data);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "bm-hist-" + Guid.NewGuid().ToString("N"));
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
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(name);
        field.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(name);
        method.Invoke(target, args);
    }
}
