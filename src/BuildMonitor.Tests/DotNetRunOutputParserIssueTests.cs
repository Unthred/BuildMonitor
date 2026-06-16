using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public class DotNetRunOutputParserIssueTests
{
    [Fact]
    public void ParseIssues_detects_unhandled_exception()
    {
        const string log = """
            info: Microsoft.Hosting.Lifetime[0]
            Unhandled exception. System.InvalidOperationException: boom
               at Program.Main()
            """;

        var issues = DotNetRunOutputParser.ParseIssues(log);

        Assert.Contains(issues, i => i.IsError && i.Text.Contains("Unhandled exception", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseIssues_ignores_dotnet_watch_banner_lines()
    {
        const string log = """
            dotnet watch 🔥 Hot reload enabled.
            dotnet watch ⌚ Building C:\src\App\App.csproj ...
            """;

        var issues = DotNetRunOutputParser.ParseIssues(log);

        Assert.Empty(issues);
    }
}
