using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class IncrementalBuildDetectorTests
{
    private const string IncrementalLog = """
        WitherbyConnect -> C:\app\bin\Debug\net9.0\WitherbyConnect.dll

        Build succeeded.
            0 Warning(s)
            0 Error(s)
        """;

    private const string FullLog = """
        C:\app\Pages\Index.razor(1,1): warning CS8618: Field required [C:\app\app.csproj]

        Build succeeded.
            1065 Warning(s)
            0 Error(s)
        """;

    [Fact]
    public void WasCompileSkipped_true_for_up_to_date_summary_without_diagnostics() =>
        Assert.True(IncrementalBuildDetector.WasCompileSkipped(IncrementalLog));

    [Fact]
    public void WasCompileSkipped_false_when_warnings_in_summary() =>
        Assert.False(IncrementalBuildDetector.WasCompileSkipped(FullLog));

    [Fact]
    public void Resolve_reuses_previous_log_counts_when_compile_skipped()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bm-prev-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "last-build.log");
        try
        {
            File.WriteAllText(logPath + ".prev", FullLog);
            File.WriteAllText(logPath, IncrementalLog);
            var (errors, warnings) = BuildIssueCountResolver.Resolve(IncrementalLog, logPath);
            Assert.Equal(0, errors);
            Assert.Equal(1065, warnings);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Resolve_reads_warning_count_from_saved_metadata_when_logs_are_incremental()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bm-meta-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "last-build.log");
        var metaPath = Path.Combine(dir, "last-build.meta.json");
        try
        {
            File.WriteAllText(logPath, IncrementalLog);
            File.WriteAllText(metaPath, """{"ErrorCount":0,"WarningCount":1065}""");

            var (_, warnings) = BuildIssueCountResolver.Resolve(IncrementalLog, logPath);
            Assert.Equal(1065, warnings);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
