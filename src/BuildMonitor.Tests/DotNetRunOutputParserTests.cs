using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public class DotNetRunOutputParserTests
{
    [Theory]
    [InlineData("Now listening on: https://localhost:44333", "https://localhost:44333")]
    [InlineData("      Listening on http://127.0.0.1:5154", "http://127.0.0.1:5154")]
    public void TryExtractListeningUrl_parses_listening_lines(string line, string expected)
    {
        var ok = DotNetRunOutputParser.TryExtractListeningUrl(line, out var url);

        Assert.True(ok);
        Assert.Equal(expected, url);
    }

    [Fact]
    public void TryExtractListeningUrl_returns_false_for_unrelated_line()
    {
        var ok = DotNetRunOutputParser.TryExtractListeningUrl("Build succeeded.", out var url);

        Assert.False(ok);
        Assert.Empty(url);
    }
}
