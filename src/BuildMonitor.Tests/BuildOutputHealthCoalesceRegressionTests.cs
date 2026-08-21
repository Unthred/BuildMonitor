using System.Reflection;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;
using BuildMonitor.Infrastructure.Services;

namespace BuildMonitor.Tests;

public class BuildOutputHealthCoalesceRegressionTests
{
    [Fact]
    public void TryCoalesceHealth_updates_snapshot_when_BuildProgressTracker_consumes_output_line()
    {
        var logsRoot = CreateTempDir();
        var dataRoot = CreateTempDir();
        try
        {
            var definition = TestProjectFactory.LocalOnly(
                displayName: "WitherbyConnect",
                rootFolder: @"C:\src\WitherbyConnectDotNet9",
                projectFile: @"C:\src\WitherbyConnectDotNet9\WitherbyConnect.csproj",
                launchProfile: "https",
                runOptions: new ProjectRunOptions { RunMode = ProjectRunMode.Watch });

            var logStore = new BuildLogStore(logsRoot);
            var triggerJournal = new BuildTriggerJournal(dataRoot);
            var burstStatsStore = new FileChangeBurstStatsStore(dataRoot);
            var trainingStore = new BuildTrainingStore(dataRoot);
            var runtime = new ProjectRuntime(
                definition,
                logStore,
                new DotNetCliRunner(),
                triggerJournal,
                burstStatsStore,
                trainingStore);

            SetPrivateField(runtime, "state", ProjectLifecycleState.Building);
            SetPrivateField(runtime, "lastBuildExitCode", -1);
            SetPrivateField(runtime, "buildErrorCount", 0);
            SetPrivateField(runtime, "buildWarningCount", 0);

            var tracker = new BuildProgressTracker();
            tracker.Reset();
            SetPrivateField(runtime, "buildProgressTracker", tracker);

            var snapshotBefore = runtime.BuildSnapshot();
            Assert.Equal(MonitorHealth.Green, snapshotBefore.Health);
            Assert.Equal(0, snapshotBefore.ErrorCount);

            // Error line that BuildProgressTracker classifies as a changed output line.
            var liveErrorLine =
                @"C:\src\WitherbyConnectDotNet9\Shared\AccountInfoSection.cs(174,42): error CS8780: A variable may not be declared within a 'not' or 'or' pattern. [C:\src\WitherbyConnectDotNet9\WitherbyConnect.csproj]";

            InvokePrivateMethod(runtime, "OnBuildOutputLine", liveErrorLine);

            // Regression invariant:
            // When BuildProgressTracker consumes a build output line, runtime must mark health dirty
            // so the coalescer can publish the updated snapshot (otherwise the UI can go stale).
            Assert.True(runtime.TryCoalesceHealth(), "Health should be published after consuming live build output.");

            var snapshotAfter = runtime.BuildSnapshot();
            Assert.Equal(ProjectLifecycleState.Building, snapshotAfter.State);
            Assert.Equal(MonitorHealth.Red, snapshotAfter.Health);
            Assert.True(snapshotAfter.ErrorCount > 0, "Live compiler errors must become visible while the build is active.");
        }
        finally
        {
            Directory.Delete(logsRoot, recursive: true);
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_when_build_is_already_in_progress_requests_immediate_health_refresh()
    {
        var logsRoot = CreateTempDir();
        var dataRoot = CreateTempDir();
        try
        {
            var definition = TestProjectFactory.LocalOnly(
                displayName: "WitherbyConnect",
                rootFolder: @"C:\src\WitherbyConnectDotNet9",
                projectFile: @"C:\src\WitherbyConnectDotNet9\WitherbyConnect.csproj",
                launchProfile: "https",
                runOptions: new ProjectRunOptions { RunMode = ProjectRunMode.Watch });

            var logStore = new BuildLogStore(logsRoot);
            var triggerJournal = new BuildTriggerJournal(dataRoot);
            var burstStatsStore = new FileChangeBurstStatsStore(dataRoot);
            var trainingStore = new BuildTrainingStore(dataRoot);
            var runtime = new ProjectRuntime(
                definition,
                logStore,
                new DotNetCliRunner(),
                triggerJournal,
                burstStatsStore,
                trainingStore);

            var requested = false;
            var immediateRequested = false;
            runtime.HealthCoalesceRequested += immediate =>
            {
                requested = true;
                immediateRequested = immediate;
            };

            // Simulate "a build is already running".
            SetPrivateField(runtime, "buildInProgress", 1);

            await runtime.BuildAsync(CancellationToken.None);

            Assert.True(requested, "Rejected rebuild should trigger a health refresh.");
            Assert.True(immediateRequested, "Rejected rebuild should force an immediate health refresh for user feedback.");
        }
        finally
        {
            Directory.Delete(logsRoot, recursive: true);
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    private static void InvokePrivateMethod(
        object instance,
        string methodName,
        params object[] arguments)
    {
        var type = instance.GetType();
        var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(instance, arguments);
    }

    private static void SetPrivateField<T>(
        object instance,
        string fieldName,
        T value)
    {
        var type = instance.GetType();
        var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bm-coalesce-reg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}

