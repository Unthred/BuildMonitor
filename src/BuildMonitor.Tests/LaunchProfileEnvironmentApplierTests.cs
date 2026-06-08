using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public class LaunchProfileEnvironmentApplierTests : IDisposable
{
    private readonly string root;
    private readonly string projectDir;

    public LaunchProfileEnvironmentApplierTests()
    {
        root = Path.Combine(Path.GetTempPath(), "BuildMonitor.Tests", Guid.NewGuid().ToString("N"));
        projectDir = Path.Combine(root, "SampleApp");
        Directory.CreateDirectory(Path.Combine(projectDir, "Properties"));

        var launchSettings = """
            {
              "profiles": {
                "http": {
                  "commandName": "Project",
                  "applicationUrl": "http://localhost:5154"
                },
                "https": {
                  "commandName": "Project",
                  "applicationUrl": "https://localhost:44333;http://localhost:5154"
                }
              }
            }
            """;

        File.WriteAllText(Path.Combine(projectDir, "Properties", "launchSettings.json"), launchSettings);
    }

    [Fact]
    public void ResolvePrimaryListenUrl_prefers_https_from_profile()
    {
        var url = LaunchProfileEnvironmentApplier.ResolvePrimaryListenUrl(
            root,
            "SampleApp/SampleApp.csproj",
            "https");

        Assert.Equal("https://localhost:44333", url);
    }

    [Fact]
    public void ResolveListenUrls_orders_https_before_http()
    {
        var urls = LaunchProfileEnvironmentApplier.ResolveListenUrls(
            root,
            "SampleApp/SampleApp.csproj",
            "https");

        Assert.Equal(2, urls.Count);
        Assert.StartsWith("https://", urls[0], StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
