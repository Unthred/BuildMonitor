using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public class LocalPortProbeTests
{
    [Fact]
    public void NormalizeBrowserUrl_keeps_localhost_for_https()
    {
        var result = LocalPortProbe.NormalizeBrowserUrl("https://localhost:44333/");

        Assert.Contains("localhost", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", result);
    }

    [Fact]
    public void NormalizeBrowserUrl_uses_loopback_for_http_localhost()
    {
        var result = LocalPortProbe.NormalizeBrowserUrl("http://localhost:5154/");

        Assert.Contains("127.0.0.1", result);
    }

    [Fact]
    public void NormalizeDisplayUrl_uses_localhost_for_http_loopback()
    {
        var result = LocalPortProbe.NormalizeDisplayUrl("http://127.0.0.1:5154/");

        Assert.Contains("localhost", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", result);
    }

    [Fact]
    public void PreferProfileDisplayUrl_uses_localhost_from_launch_profile()
    {
        var result = LocalPortProbe.PreferProfileDisplayUrl(
            "http://127.0.0.1:5154",
            ["http://localhost:5154"]);

        Assert.Equal("http://localhost:5154/", result);
    }
}
