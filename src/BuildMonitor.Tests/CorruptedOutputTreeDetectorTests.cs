using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class CorruptedOutputTreeDetectorTests
{
    [Fact]
    public void IsCorruptedTreeFailure_detects_nested_artifacts_path_in_log()
    {
        const string log = """
            error MSB3030: Could not copy the file "appsettings.json" because it was not found.
            Source: C:\repo\artifacts\build\bin\Debug\net9.0\artifacts\build\bin\Debug\net9.0\appsettings.json
            """;

        Assert.True(CorruptedOutputTreeDetector.IsCorruptedTreeFailure(log));
    }

    [Fact]
    public void IsCorruptedTreeFailure_ignores_normal_compile_error()
    {
        const string log = "error CS1002: ; expected";

        Assert.False(CorruptedOutputTreeDetector.IsCorruptedTreeFailure(log));
    }

    [Fact]
    public void HasRiskyBaseOutputPath_detects_custom_output_property()
    {
        Assert.True(CorruptedOutputTreeDetector.HasRiskyBaseOutputPath("-p:BaseOutputPath=artifacts/build/"));
        Assert.False(CorruptedOutputTreeDetector.HasRiskyBaseOutputPath("--verbosity quiet"));
    }

    [Fact]
    public void HasNestedArtifactsOnDisk_detects_nested_build_tree()
    {
        var root = Path.Combine(Path.GetTempPath(), "bm-detector-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "artifacts", "build", "tmp", "artifacts", "build");
        try
        {
            Directory.CreateDirectory(nested);
            Assert.True(CorruptedOutputTreeDetector.HasNestedArtifactsOnDisk(root));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }
}
