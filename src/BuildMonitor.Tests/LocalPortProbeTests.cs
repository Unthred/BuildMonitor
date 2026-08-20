using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public class LocalPortProbeTests
{
    private static readonly string[] WitherbyProfileUrls =
    [
        "http://localhost:5154",
        "https://localhost:44333"
    ];

    [Fact]
    public void Multiple_profile_urls_prefers_https_localhost()
    {
        var canonical = LocalPortProbe.ResolveCanonicalUserFacingUrl(null, WitherbyProfileUrls);

        Assert.Equal("https://localhost:44333/", canonical);
    }

    [Fact]
    public void Runtime_ip_https_maps_to_profile_localhost()
    {
        var canonical = LocalPortProbe.ResolveCanonicalUserFacingUrl(
            "https://127.0.0.1:44333",
            ["https://localhost:44333"]);

        Assert.Equal("https://localhost:44333/", canonical);
    }

    [Fact]
    public void Runtime_ip_http_maps_to_profile_localhost()
    {
        var canonical = LocalPortProbe.ResolveCanonicalUserFacingUrl(
            "http://127.0.0.1:5154",
            ["http://localhost:5154"]);

        Assert.Equal("http://localhost:5154/", canonical);
    }

    [Fact]
    public void Runtime_only_https_loopback_preserved_when_no_profile_match()
    {
        var canonical = LocalPortProbe.ResolveCanonicalUserFacingUrl(
            "https://127.0.0.1:44333",
            []);

        Assert.Equal("https://127.0.0.1:44333/", canonical);
    }

    [Fact]
    public void Open_endpoints_prefers_https_localhost_when_both_active()
    {
        var canonical = LocalPortProbe.ResolveCanonicalUserFacingUrlFromOpenEndpoints(
            ["http://localhost:5154", "https://127.0.0.1:44333"],
            WitherbyProfileUrls);

        Assert.Equal("https://localhost:44333/", canonical);
    }

    [Fact]
    public void Open_http_only_still_canonicalizes_to_localhost()
    {
        var canonical = LocalPortProbe.ResolveCanonicalUserFacingUrlFromOpenEndpoints(
            ["http://127.0.0.1:5154"],
            WitherbyProfileUrls);

        Assert.Equal("http://localhost:5154/", canonical);
    }

    [Fact]
    public void Canonical_url_is_identical_for_display_and_browser()
    {
        var runtime = "https://127.0.0.1:44333";
        var profile = new[] { "https://localhost:44333" };

        var display = LocalPortProbe.ResolveCanonicalUserFacingUrl(runtime, profile);
        var browser = LocalPortProbe.ResolveCanonicalUserFacingUrl(runtime, profile);

        Assert.Equal(display, browser);
        Assert.Equal("https://localhost:44333/", display);
        Assert.DoesNotContain("127.0.0.1", display);
    }

    [Fact]
    public void ResolveListenUrls_orders_https_localhost_before_http()
    {
        var root = Path.Combine(Path.GetTempPath(), "bm-url-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Properties"));
        File.WriteAllText(
            Path.Combine(root, "Properties", "launchSettings.json"),
            """
            {
              "profiles": {
                "https": {
                  "applicationUrl": "http://localhost:5154;https://localhost:44333"
                }
              }
            }
            """);
        try
        {
            var urls = LaunchProfileEnvironmentApplier.ResolveListenUrls(root, "App.csproj", "https");

            Assert.Equal(2, urls.Count);
            Assert.Equal("https://localhost:44333", urls[0]);
            Assert.Equal("http://localhost:5154", urls[1]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
