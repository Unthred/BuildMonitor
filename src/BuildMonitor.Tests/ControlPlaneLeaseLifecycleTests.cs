using System.Reflection;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;
using BuildMonitor.Infrastructure.Services;

namespace BuildMonitor.Tests;

/// <summary>
/// Lease retirement / cancel race invariants for exclusive control-plane operations.
/// </summary>
public sealed class ControlPlaneLeaseLifecycleTests
{
    [Fact]
    public void Finish_before_cancel_returns_conflict_and_leaves_outcome_untargetable()
    {
        using var env = CreateRuntimeEnvironment();
        var lease = InvokeAcquire(env.Runtime, ControlPlaneOperationKind.Rebuild);
        Assert.NotNull(GetActiveLease(env.Runtime));

        InvokeRetire(env.Runtime, lease, ControlPlaneOperationKind.Rebuild);

        Assert.Null(GetActiveLease(env.Runtime));
        Assert.Equal(0, GetPrivateField<int>(env.Runtime, "agentRebuildInProgress"));

        var ex = Assert.Throws<InvalidOperationException>(
            () => env.Runtime.RequestCancelControlPlaneOperation(lease.OperationId));
        Assert.Contains("No cancellable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_old_new_overlap_until_old_lease_retired()
    {
        using var env = CreateRuntimeEnvironment();
        var oldLease = InvokeAcquire(env.Runtime, ControlPlaneOperationKind.Tests);

        var overlap = Assert.Throws<TargetInvocationException>(
            () => InvokeAcquire(env.Runtime, ControlPlaneOperationKind.Rebuild));
        Assert.IsType<InvalidOperationException>(overlap.InnerException);
        Assert.Contains("lease is still active", overlap.InnerException!.Message, StringComparison.OrdinalIgnoreCase);

        // Old cancel still targets the old lease while it is active.
        var cancelWhileOld = env.Runtime.RequestCancelControlPlaneOperation(oldLease.OperationId);
        Assert.True(cancelWhileOld.CancelRequested);
        Assert.Equal(oldLease.OperationId, cancelWhileOld.OperationId);

        InvokeRetire(env.Runtime, oldLease, ControlPlaneOperationKind.Tests);

        var newLease = InvokeAcquire(env.Runtime, ControlPlaneOperationKind.Rebuild);
        Assert.NotEqual(oldLease.OperationId, newLease.OperationId);

        // Stale cancel against the retired operation id cannot hit the new lease.
        var stale = Assert.Throws<InvalidOperationException>(
            () => env.Runtime.RequestCancelControlPlaneOperation(oldLease.OperationId));
        Assert.Contains("does not match", stale.Message, StringComparison.OrdinalIgnoreCase);

        var fresh = env.Runtime.RequestCancelControlPlaneOperation(newLease.OperationId);
        Assert.Equal(newLease.OperationId, fresh.OperationId);
        Assert.False(fresh.AlreadyRequested);

        InvokeRetire(env.Runtime, newLease, ControlPlaneOperationKind.Rebuild);
    }

    [Fact]
    public void Duplicate_cancel_while_genuinely_active_is_idempotent()
    {
        using var env = CreateRuntimeEnvironment();
        var lease = InvokeAcquire(env.Runtime, ControlPlaneOperationKind.ShipCheck);

        var first = env.Runtime.RequestCancelControlPlaneOperation(null);
        Assert.True(first.CancelRequested);
        Assert.False(first.AlreadyRequested);

        var second = env.Runtime.RequestCancelControlPlaneOperation(lease.OperationId);
        Assert.True(second.CancelRequested);
        Assert.True(second.AlreadyRequested);
        Assert.Equal(lease.OperationId, second.OperationId);

        InvokeRetire(env.Runtime, lease, ControlPlaneOperationKind.ShipCheck);

        Assert.Throws<InvalidOperationException>(
            () => env.Runtime.RequestCancelControlPlaneOperation(lease.OperationId));
    }

    [Fact]
    public void Retire_clears_lease_before_exclusive_flag_is_observable_as_zero_with_null_lease()
    {
        using var env = CreateRuntimeEnvironment();
        var lease = InvokeAcquire(env.Runtime, ControlPlaneOperationKind.Rebuild);
        Assert.Equal(1, GetPrivateField<int>(env.Runtime, "agentRebuildInProgress"));
        Assert.NotNull(GetActiveLease(env.Runtime));

        InvokeRetire(env.Runtime, lease, ControlPlaneOperationKind.Rebuild);

        // Never leave flag=0 with an old lease still installed (or the reverse).
        Assert.Null(GetActiveLease(env.Runtime));
        Assert.Equal(0, GetPrivateField<int>(env.Runtime, "agentRebuildInProgress"));
    }

    private static ControlPlaneOperationLease InvokeAcquire(
        ProjectRuntime runtime,
        ControlPlaneOperationKind kind)
    {
        var method = typeof(ProjectRuntime).GetMethod(
            "TryAcquireControlPlaneLease",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<ControlPlaneOperationLease>(method!.Invoke(runtime, [kind]));
    }

    private static void InvokeRetire(
        ProjectRuntime runtime,
        ControlPlaneOperationLease lease,
        ControlPlaneOperationKind kind)
    {
        var method = typeof(ProjectRuntime).GetMethod(
            "RetireControlPlaneOperation",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(runtime, [lease, kind]);
    }

    private static ControlPlaneOperationLease? GetActiveLease(ProjectRuntime runtime) =>
        GetPrivateField<ControlPlaneOperationLease?>(runtime, "activeControlPlaneLease");

    private static RuntimeEnvironment CreateRuntimeEnvironment()
    {
        var logsRoot = CreateTempDir();
        var dataRoot = CreateTempDir();
        var definition = TestProjectFactory.LocalOnly(
            displayName: "Demo",
            id: "demo-lease",
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

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "bm-lease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field!.GetValue(target)!;
    }

    private sealed class RuntimeEnvironment(string logsRoot, string dataRoot, ProjectRuntime runtime) : IDisposable
    {
        public ProjectRuntime Runtime { get; } = runtime;

        public void Dispose()
        {
            Runtime.Dispose();
            try
            {
                Directory.Delete(logsRoot, recursive: true);
            }
            catch
            {
                // ignore
            }

            try
            {
                Directory.Delete(dataRoot, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }
}
