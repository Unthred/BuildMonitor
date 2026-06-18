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
            Build succeeded.
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
}
