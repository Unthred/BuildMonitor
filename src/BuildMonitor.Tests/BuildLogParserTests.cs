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
    public void ParseErrorCount_uses_last_summary_line_when_present()
    {
        const string log = """
            Pages\EbookError.razor(127,17): warning CS8602: Dereference of a possibly null reference.
            Build succeeded.
                1056 Warning(s)
                0 Error(s)
            """;

        Assert.Equal(0, BuildLogParser.ParseErrorCount(log));
        Assert.Equal(1056, BuildLogParser.ParseWarningCount(log));
    }
}
