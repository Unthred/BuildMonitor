using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public class BuildLogParserTests
{
    [Fact]
    public void ParseErrorCount_counts_compiler_errors()
    {
        const string log = """
            Pages\Foo.cs(10,5): error CS1002: ; expected
            Pages\Bar.cs(2,1): warning CS0168: unused variable
            """;

        Assert.Equal(1, BuildLogParser.ParseErrorCount(log));
    }

    [Fact]
    public void ParseWarningCount_counts_compiler_warnings()
    {
        const string log = """
            Pages\Bar.cs(2,1): warning CS0168: unused variable
            Build succeeded with 1 warning(s) in 1.0s
            """;

        Assert.Equal(1, BuildLogParser.ParseWarningCount(log));
    }

    [Fact]
    public void ParseWarningCount_reads_terminal_logger_build_summary()
    {
        const string log = """
            WitherbyConnect succeeded with 1043 warning(s) (12.3s) -> bin\Debug\net8.0\WitherbyConnect.dll
            Build succeeded with 1043 warning(s) in 45.2s
            """;

        Assert.Equal(1043, BuildLogParser.ParseWarningCount(log));
    }

    [Fact]
    public void ParseWarningCount_uses_latest_build_result_in_watch_output()
    {
        const string log = """
            Build succeeded with 1043 warning(s) in 45.2s
            dotnet watch ⌚ File changed: ./Foo.cs
            dotnet watch ⌚ Building ...
            Build succeeded with 0 warning(s) in 1.2s
            """;

        Assert.Equal(0, BuildLogParser.ParseWarningCount(log));
    }

    [Fact]
    public void ParseErrorCount_reads_combined_terminal_logger_summary()
    {
        const string log = "Build failed with 2 error(s) and 17 warning(s) in 3.4s";

        Assert.Equal(2, BuildLogParser.ParseErrorCount(log));
        Assert.Equal(17, BuildLogParser.ParseWarningCount(log));
    }

    [Fact]
    public void ParseWarningCount_explicit_zero_summary_not_overridden_by_incremental_note()
    {
        const string log = """
            Build succeeded.
                0 Warning(s)
                0 Error(s)

            [BuildMonitor] Incremental build — compiler skipped (outputs up-to-date). Tray health uses 1065 warning(s) from the previous full build log.
            """;

        Assert.Equal(0, BuildLogParser.ParseWarningCount(log));
        Assert.Equal(0, BuildLogParser.ParseErrorCount(log));
        Assert.Equal((0, 1065), BuildLogParser.TryParseIncrementalHealthNote(log));
    }

    [Fact]
    public void ParseErrorCount_explicit_zero_summary_not_overridden_by_incremental_note_with_errors()
    {
        const string log = """
            Build succeeded.
                0 Warning(s)
                0 Error(s)

            [BuildMonitor] Incremental build — compiler skipped (outputs up-to-date). Tray health uses 6 error(s), 2000 warning(s) from the previous full build log.
            """;

        Assert.Equal(0, BuildLogParser.ParseErrorCount(log));
        Assert.Equal(0, BuildLogParser.ParseWarningCount(log));
        Assert.Equal((6, 2000), BuildLogParser.TryParseIncrementalHealthNote(log));
    }

    [Fact]
    public void ParseErrorCount_finds_msbuild_error_before_build_failed_line()
    {
        const string log = """
            [BuildMonitor] ===== Build #3 started 2026-06-24 10:00:00 — file change =====
            C:\proj\Microsoft.NET.Sdk.StaticWebAssets.Compression.targets(269,5): error : The asset 'C:\proj\obj\Debug\net9.0\compressed\foo.gz' can not be found at any of the searched locations 'wwwroot\css\app.css'.
            Build FAILED.
            C:\proj\Microsoft.NET.Sdk.StaticWebAssets.Compression.targets(269,5): error : The asset 'C:\proj\obj\Debug\net9.0\compressed\foo.gz' can not be found at any of the searched locations 'wwwroot\css\app.css'.
            """;

        Assert.Equal(1, BuildLogParser.ParseErrorCount(log));
    }

    [Fact]
    public void ResolveBuildIssues_uses_current_log_only()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bm-prev-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var currentPath = Path.Combine(dir, "last-build.log");
        var prevPath = currentPath + ".prev";
        try
        {
            File.WriteAllText(prevPath, """
                C:\app\Foo.cs(1,1): warning CS8618: required [C:\app\app.csproj]

                Build succeeded.
                    1 Warning(s)
                    0 Error(s)
                """);
            File.WriteAllText(currentPath, """
                Build succeeded.
                    0 Warning(s)
                    0 Error(s)
                """);

            var issues = BuildLogParser.ResolveBuildIssues(File.ReadAllText(currentPath), currentPath);
            Assert.Empty(issues);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveBuildIssues_does_not_carry_forward_errors_from_previous_log()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bm-prev-err-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var currentPath = Path.Combine(dir, "last-build.log");
        var prevPath = currentPath + ".prev";
        try
        {
            File.WriteAllText(prevPath, """
                C:\app\Foo.cs(1,1): error CS1002: ; expected [C:\app\app.csproj]
                C:\app\Bar.cs(1,1): warning CS8618: required [C:\app\app.csproj]

                Build FAILED.
                    1 Warning(s)
                    1 Error(s)
                """);
            File.WriteAllText(currentPath, """
                Build succeeded.
                    0 Warning(s)
                    0 Error(s)
                """);

            var issues = BuildLogParser.ResolveBuildIssues(File.ReadAllText(currentPath), currentPath);
            Assert.Empty(issues);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
