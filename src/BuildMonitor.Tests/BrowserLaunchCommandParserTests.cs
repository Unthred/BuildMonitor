using BuildMonitor.Infrastructure.Navigation;

namespace BuildMonitor.Tests;

public sealed class BrowserLaunchCommandParserTests
{
    [Theory]
    [InlineData(@"""C:\Program Files\Google\Chrome\Application\chrome.exe"" --single-argument %1", @"C:\Program Files\Google\Chrome\Application\chrome.exe")]
    [InlineData(@"msedge.exe", "msedge.exe")]
    public void TryExtractExecutablePath_parses_registry_command(string command, string expected)
    {
        var path = BrowserLaunchCommandParser.TryExtractExecutablePath(command);
        Assert.Equal(expected, path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryExtractExecutablePath_returns_null_for_empty(string? command)
    {
        Assert.Null(BrowserLaunchCommandParser.TryExtractExecutablePath(command));
    }
}
