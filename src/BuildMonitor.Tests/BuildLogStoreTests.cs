using BuildMonitor.Core.Models;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class BuildLogStoreTests
{
    private const string FullLog = """
        C:\app\Pages\Index.razor(1,1): warning CS8618: Field required [C:\app\app.csproj]

        Build succeeded.
            1065 Warning(s)
            0 Error(s)
        """;

    private const string IncrementalLog = """
        WitherbyConnect -> C:\app\bin\Debug\net9.0\WitherbyConnect.dll

        Build succeeded.
            0 Warning(s)
            0 Error(s)
        """;

    private const string RebuildLog = """
        C:\app\Foo.cs(1,1): warning CA2200: rethrow [C:\app\app.csproj]

        Build succeeded.
            1032 Warning(s)
            0 Error(s)
        """;

    [Fact]
    public async Task SaveAsync_incremental_zero_summary_does_not_keep_prior_warnings()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bm-store-{Guid.NewGuid():N}");
        var store = new BuildLogStore(dir);
        const string projectId = "demo";
        var started = DateTimeOffset.UtcNow;

        try
        {
            await store.SaveAsync(
                projectId,
                BuildLogKind.Build,
                "dotnet build",
                exitCode: 0,
                started,
                FullLog);

            var incremental = await store.SaveAsync(
                projectId,
                BuildLogKind.Build,
                "dotnet build",
                exitCode: 0,
                started,
                IncrementalLog);

            Assert.Equal(0, incremental.ErrorCount);
            Assert.Equal(0, incremental.WarningCount);

            var loaded = await store.LoadMetadataAsync(projectId, BuildLogKind.Build);
            Assert.NotNull(loaded);
            Assert.Equal(0, loaded.WarningCount);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_rebuild_replaces_prior_warning_count()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bm-store-rebuild-{Guid.NewGuid():N}");
        var store = new BuildLogStore(dir);
        const string projectId = "demo";
        var started = DateTimeOffset.UtcNow;

        try
        {
            await store.SaveAsync(
                projectId,
                BuildLogKind.Build,
                "dotnet build",
                exitCode: 0,
                started,
                FullLog);

            var rebuilt = await store.SaveAsync(
                projectId,
                BuildLogKind.Build,
                "dotnet build --no-incremental",
                exitCode: 0,
                started,
                RebuildLog);

            Assert.Equal(0, rebuilt.ErrorCount);
            Assert.Equal(1032, rebuilt.WarningCount);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_successful_build_clears_stale_error_counts()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bm-store-err-{Guid.NewGuid():N}");
        var store = new BuildLogStore(dir);
        const string projectId = "demo";
        var started = DateTimeOffset.UtcNow;

        const string failedLog = """
            C:\app\Foo.cs(1,1): error CS1002: ; expected [C:\app\app.csproj]

            Build FAILED.
                0 Warning(s)
                6 Error(s)
            """;

        try
        {
            await store.SaveAsync(
                projectId,
                BuildLogKind.Build,
                "dotnet build",
                exitCode: 1,
                started,
                failedLog);

            var succeeded = await store.SaveAsync(
                projectId,
                BuildLogKind.Build,
                "dotnet build",
                exitCode: 0,
                started,
                IncrementalLog);

            Assert.Equal(0, succeeded.ErrorCount);
            Assert.Empty(succeeded.ErrorLines);

            var loaded = await store.LoadMetadataAsync(projectId, BuildLogKind.Build);
            Assert.NotNull(loaded);
            Assert.Equal(0, loaded.ErrorCount);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
