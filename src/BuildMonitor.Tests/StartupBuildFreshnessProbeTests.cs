using System.Diagnostics;
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
        if (!Directory.Exists(root))
        {
            return;
        }

        // Git marks object files read-only.
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(root, recursive: true);
    }

    private string WriteFile(string relativePath, DateTime writtenUtc)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, writtenUtc);
        return path;
    }

    private DateTimeOffset? Newest() =>
        StartupBuildFreshnessProbe.FindNewestSourceWriteUtc(
            root,
            WatchExcludeSegments.DefaultSegmentSet,
            CancellationToken.None);

    private void Git(params string[] arguments)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    [Fact]
    public void Newest_source_ignores_excluded_folders_and_noise_files()
    {
        WriteFile(@"src\App.cs", Old);
        WriteFile(@"bin\Debug\App.dll", Newer);
        WriteFile(@"obj\project.assets.json", Newer);
        WriteFile(@"docs\notes.txt", Newer);
        WriteFile(@"src\README.md", Newer);

        Assert.Equal(new DateTimeOffset(Old), Newest());
    }

    [Fact]
    public void Newest_source_picks_latest_meaningful_file()
    {
        WriteFile(@"src\App.cs", Old);
        WriteFile(@"src\Pages\Index.razor", Newer);

        Assert.Equal(new DateTimeOffset(Newer), Newest());
    }

    [Fact]
    public void Newest_source_skips_files_git_ignores()
    {
        Git("init", "-q");
        WriteFile(".gitignore", Old);
        File.WriteAllText(Path.Combine(root, ".gitignore"), "wwwroot/js/generated.js\n");
        File.SetLastWriteTimeUtc(Path.Combine(root, ".gitignore"), Old);
        WriteFile(@"src\App.cs", Old);
        WriteFile(@"src\Untracked.cs", Old);
        Git("add", ".gitignore", "src/App.cs");

        // Written by the build into the source tree, and gitignored.
        WriteFile(@"wwwroot\js\generated.js", Newer);

        Assert.Equal(new DateTimeOffset(Old), Newest());
    }

    [Fact]
    public void Newest_source_counts_untracked_files_git_does_not_ignore()
    {
        Git("init", "-q");
        WriteFile(@"src\App.cs", Old);
        Git("add", "src/App.cs");
        WriteFile(@"src\NewPage.razor", Newer);

        Assert.Equal(new DateTimeOffset(Newer), Newest());
    }

    [Fact]
    public void Newest_source_is_null_for_missing_folder() =>
        Assert.Null(StartupBuildFreshnessProbe.FindNewestSourceWriteUtc(
            Path.Combine(root, "missing"),
            WatchExcludeSegments.DefaultSegmentSet,
            CancellationToken.None));

    [Fact]
    public void Output_time_uses_only_the_run_configuration()
    {
        var project = WriteFile(@"App\App.csproj", Old);
        WriteFile(@"App\bin\Debug\net10.0\App.dll", Newer);
        WriteFile(@"App\bin\Release\net10.0\App.dll", Old);

        Assert.Equal(
            new DateTimeOffset(Newer),
            StartupBuildFreshnessProbe.FindBuildOutputWrittenUtc(project, "Debug"));
        Assert.Equal(
            new DateTimeOffset(Old),
            StartupBuildFreshnessProbe.FindBuildOutputWrittenUtc(project, "Release"));
    }

    [Fact]
    public void Output_time_is_oldest_copy_across_target_frameworks()
    {
        var project = WriteFile(@"App\App.csproj", Old);
        WriteFile(@"App\bin\Debug\net9.0\App.dll", Old);
        WriteFile(@"App\bin\Debug\net10.0\App.dll", Newer);

        Assert.Equal(
            new DateTimeOffset(Old),
            StartupBuildFreshnessProbe.FindBuildOutputWrittenUtc(project, "Debug"));
    }

    [Fact]
    public void Output_time_is_null_without_project_assembly()
    {
        var project = WriteFile(@"App\App.csproj", Old);
        WriteFile(@"App\bin\Debug\net10.0\Other.dll", Newer);

        Assert.Null(StartupBuildFreshnessProbe.FindBuildOutputWrittenUtc(project, "Debug"));
    }

    [Fact]
    public void Output_time_is_null_without_configuration_folder()
    {
        var project = WriteFile(@"App\App.csproj", Old);
        WriteFile(@"App\bin\Release\net10.0\App.dll", Newer);

        Assert.Null(StartupBuildFreshnessProbe.FindBuildOutputWrittenUtc(project, "Debug"));
    }

    [Theory]
    [InlineData(null, "Debug")]
    [InlineData("", "Debug")]
    [InlineData("--no-restore", "Debug")]
    [InlineData("-c Release", "Release")]
    [InlineData("--configuration Staging", "Staging")]
    [InlineData("-c:Release", "Release")]
    [InlineData("--configuration=Release", "Release")]
    [InlineData("--no-restore -c Release --verbosity q", "Release")]
    public void Configuration_comes_from_extra_args(string? extraArgs, string expected) =>
        Assert.Equal(expected, StartupBuildFreshnessProbe.ResolveConfiguration(extraArgs));
}
