using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public class DotNetRunLaunchProfileArgsTests : IDisposable
{
    private readonly string root;

    public DotNetRunLaunchProfileArgsTests()
    {
        root = Path.Combine(Path.GetTempPath(), "bm-launch-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Properties"));
        File.WriteAllText(
            Path.Combine(root, "Properties", "launchSettings.json"),
            """
            {
              "profiles": {
                "https": {
                  "commandName": "Project",
                  "applicationUrl": "https://localhost:44333;http://localhost:5154"
                }
              }
            }
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveEffectiveLaunchProfile_uses_configured_name()
    {
        var profile = LaunchProfileEnvironmentApplier.ResolveEffectiveLaunchProfile(
            root,
            "App.csproj",
            "https");

        Assert.Equal("https", profile);
    }

    [Fact]
    public void ResolvePrimaryListenUrl_prefers_https_from_profile()
    {
        var url = LaunchProfileEnvironmentApplier.ResolvePrimaryListenUrl(
            root,
            "App.csproj",
            "https");

        Assert.Equal("https://localhost:44333", url);
    }
}
