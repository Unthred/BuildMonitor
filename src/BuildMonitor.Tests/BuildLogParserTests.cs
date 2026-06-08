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
}
