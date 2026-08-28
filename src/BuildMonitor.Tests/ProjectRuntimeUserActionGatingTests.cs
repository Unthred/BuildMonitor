using System.Reflection;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;
using BuildMonitor.Infrastructure.Services;

namespace BuildMonitor.Tests;

/// <summary>
/// Regression: user rebuild/tests gates must not stay blocked after compile finishes while post-build work runs.
/// </summary>
public sealed class ProjectRuntimeUserActionGatingTests
{
    [Fact]
    public void Manual_test_run_blocked_only_during_compile_or_active_test()
    {
        using var env = CreateRuntimeEnvironment();

        Assert.False(env.Runtime.IsManualTestRunBlocked);

        SetPrivateField(env.Runtime, "compileInProgress", 1);
        Assert.True(env.Runtime.IsManualTestRunBlocked);

        SetPrivateField(env.Runtime, "compileInProgress", 0);
        SetPrivateField(env.Runtime, "testInProgress", 1);
        Assert.True(env.Runtime.IsManualTestRunBlocked);

        SetPrivateField(env.Runtime, "testInProgress", 0);
        SetPrivateField(env.Runtime, "buildInProgress", 1);
        Assert.False(env.Runtime.IsManualTestRunBlocked);
    }

    [Fact]
    public void Compile_finished_while_build_session_active_releases_manual_test_gate()
    {
        using var env = CreateRuntimeEnvironment();

        SetPrivateField(env.Runtime, "buildInProgress", 1);
        SetPrivateField(env.Runtime, "compileInProgress", 0);

        Assert.False(env.Runtime.IsManualTestRunBlocked);
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
            Runtime.Dispose();
            Directory.Delete(LogsRoot, recursive: true);
            Directory.Delete(DataRoot, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "bm-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
