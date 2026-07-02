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

    [Fact]
    public async Task SaveAsync_preserves_warning_counts_when_incremental_overwrites_log()
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
            Assert.Equal(1065, incremental.WarningCount);

            var loaded = await store.LoadMetadataAsync(projectId, BuildLogKind.Build);
            Assert.NotNull(loaded);
            Assert.Equal(1065, loaded.WarningCount);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
