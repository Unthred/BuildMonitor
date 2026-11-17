using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class StartupBuildFreshnessProbeTests : IDisposable
{
    private static readonly DateTime Old = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newer = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "bm-startup-probe-" + Guid.NewGuid().ToString("N"));

    public StartupBuildFreshnessProbeTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string WriteFile(string relativePath, DateTime writtenUtc)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, writtenUtc);
        return path;
    }

    [Fact]
    public void Newest_source_ignores_excluded_folders_and_noise_files()
    {
        WriteFile(@"src\App.cs", Old);
        WriteFile(@"bin\Debug\App.dll", Newer);
        WriteFile(@"obj\project.assets.json", Newer);
        WriteFile(@"docs\notes.txt", Newer);
        WriteFile(@"src\README.md", Newer);

        var newest = StartupBuildFreshnessProbe.FindNewestSourceWriteUtc(
            root,
            WatchExcludeSegments.DefaultSegmentSet,
            CancellationToken.None);

        Assert.Equal(new DateTimeOffset(Old), newest);
    }

    [Fact]
    public void Newest_source_picks_latest_meaningful_file()
    {
        WriteFile(@"src\App.cs", Old);
        WriteFile(@"src\Pages\Index.razor", Newer);

        var newest = StartupBuildFreshnessProbe.FindNewestSourceWriteUtc(
            root,
            WatchExcludeSegments.DefaultSegmentSet,
            CancellationToken.None);

        Assert.Equal(new DateTimeOffset(Newer), newest);
    }

    [Fact]
    public void Newest_source_is_null_for_missing_folder() =>
        Assert.Null(StartupBuildFreshnessProbe.FindNewestSourceWriteUtc(
            Path.Combine(root, "missing"),
            WatchExcludeSegments.DefaultSegmentSet,
            CancellationToken.None));

    [Fact]
    public void Output_time_is_oldest_copy_of_project_assembly()
    {
        var project = WriteFile(@"App\App.csproj", Old);
        WriteFile(@"App\bin\Debug\net10.0\App.dll", Old);
        WriteFile(@"App\bin\Release\net10.0\App.dll", Newer);

        Assert.Equal(
            new DateTimeOffset(Old),
            StartupBuildFreshnessProbe.FindBuildOutputWrittenUtc(project));
    }

    [Fact]
    public void Output_time_is_null_without_project_assembly()
    {
        var project = WriteFile(@"App\App.csproj", Old);
        WriteFile(@"App\bin\Debug\net10.0\Other.dll", Newer);

        Assert.Null(StartupBuildFreshnessProbe.FindBuildOutputWrittenUtc(project));
    }

    [Fact]
    public void Output_time_is_null_without_bin_folder()
    {
        var project = WriteFile(@"App\App.csproj", Old);

        Assert.Null(StartupBuildFreshnessProbe.FindBuildOutputWrittenUtc(project));
    }
}
