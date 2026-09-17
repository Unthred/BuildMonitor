using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class OutputLockReleaserTests
{
    [Fact]
    public void Same_assembly_name_under_another_root_is_not_owned()
    {
        var firstRoot = Path.Combine(Path.GetTempPath(), "bm-lock-a-" + Guid.NewGuid().ToString("N"));
        var secondRoot = Path.Combine(Path.GetTempPath(), "bm-lock-b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);

        try
        {
            var siblingExe = Path.Combine(secondRoot, "bin", "Debug", "net10.0", "App.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(siblingExe)!);

            Assert.False(OutputLockReleaser.IsOwnedByProjectRoot(
                "App",
                siblingExe,
                "App",
                firstRoot));
            Assert.True(OutputLockReleaser.IsOwnedByProjectRoot(
                "App",
                siblingExe,
                "App",
                secondRoot));
        }
        finally
        {
            TryDelete(firstRoot);
            TryDelete(secondRoot);
        }
    }

    [Fact]
    public void Matching_process_name_without_path_under_root_is_not_owned()
    {
        Assert.False(OutputLockReleaser.IsOwnedByProjectRoot(
            "App",
            executablePath: null,
            "App",
            @"C:\src\App"));
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
