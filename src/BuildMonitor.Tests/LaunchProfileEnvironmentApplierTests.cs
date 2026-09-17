using System.Diagnostics;
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
        WriteProfile(
            projectDir,
            """
            {
              "profiles": {
                "http": {
                  "commandName": "Project",
                  "applicationUrl": "http://localhost:5154"
                },
                "https": {
                  "commandName": "Project",
                  "applicationUrl": "https://localhost:44333;http://localhost:5154",
                  "environmentVariables": {
                    "ASPNETCORE_ENVIRONMENT": "Development"
                  }
                }
              }
            }
            """);
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

    [Fact]
    public void ResolvePrimaryListenUrl_uses_https_profile_when_configured_profile_empty()
    {
        var url = LaunchProfileEnvironmentApplier.ResolvePrimaryListenUrl(
            root,
            "SampleApp/SampleApp.csproj",
            null);

        Assert.Equal("https://localhost:44333", url);
    }

    [Fact]
    public void ResolveListenUrls_reads_ASPNETCORE_URLS_from_profile_environment()
    {
        var envOnlyDir = Path.Combine(root, "EnvApp");
        Directory.CreateDirectory(Path.Combine(envOnlyDir, "Properties"));
        WriteProfile(
            envOnlyDir,
            """
            {
              "profiles": {
                "Dev": {
                  "commandName": "Project",
                  "environmentVariables": {
                    "ASPNETCORE_URLS": "https://localhost:7001;http://localhost:5001"
                  }
                }
              }
            }
            """);

        var urls = LaunchProfileEnvironmentApplier.ResolveListenUrls(
            root,
            "EnvApp/EnvApp.csproj",
            "Dev");

        Assert.Equal(2, urls.Count);
        Assert.StartsWith("https://", urls[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyTo_copies_profile_environment_onto_the_child_only()
    {
        var previous = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
            var psi = new ProcessStartInfo();
            var result = LaunchProfileEnvironmentApplier.ApplyTo(
                psi,
                root,
                "SampleApp/SampleApp.csproj",
                "https");

            Assert.Equal("Development", psi.Environment["ASPNETCORE_ENVIRONMENT"]);
            Assert.Equal("Development", result.EnvironmentName);
            Assert.Equal("https", result.LaunchProfile);
            Assert.Equal("Production", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previous);
        }
    }

    [Fact]
    public void ApplyTo_effective_url_wins_over_profile_ASPNETCORE_URLS_and_applicationUrl()
    {
        var conflictDir = Path.Combine(root, "ConflictApp");
        Directory.CreateDirectory(Path.Combine(conflictDir, "Properties"));
        WriteProfile(
            conflictDir,
            """
            {
              "profiles": {
                "https": {
                  "commandName": "Project",
                  "applicationUrl": "https://localhost:44333",
                  "environmentVariables": {
                    "ASPNETCORE_ENVIRONMENT": "Development",
                    "ASPNETCORE_URLS": "https://localhost:7001"
                  }
                }
              }
            }
            """);

        var psi = new ProcessStartInfo();
        var result = LaunchProfileEnvironmentApplier.ApplyTo(
            psi,
            root,
            "ConflictApp/ConflictApp.csproj",
            "https",
            applicationUrlOverride: "https://localhost:44349;http://localhost:5170");

        Assert.Equal("https://localhost:44349;http://localhost:5170", psi.Environment["ASPNETCORE_URLS"]);
        Assert.Equal("https://localhost:44349;http://localhost:5170", result.EffectiveUrls);
        Assert.Equal("Development", psi.Environment["ASPNETCORE_ENVIRONMENT"]);
        Assert.False(psi.Environment["ASPNETCORE_URLS"].Contains("7001", StringComparison.Ordinal));
        Assert.False(psi.Environment["ASPNETCORE_URLS"].Contains("44333", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyTo_uses_profile_environment_ASPNETCORE_URLS_before_applicationUrl()
    {
        var conflictDir = Path.Combine(root, "EnvWinsApp");
        Directory.CreateDirectory(Path.Combine(conflictDir, "Properties"));
        WriteProfile(
            conflictDir,
            """
            {
              "profiles": {
                "https": {
                  "commandName": "Project",
                  "applicationUrl": "https://localhost:44333",
                  "environmentVariables": {
                    "ASPNETCORE_URLS": "https://localhost:7001"
                  }
                }
              }
            }
            """);

        var psi = new ProcessStartInfo();
        var result = LaunchProfileEnvironmentApplier.ApplyTo(
            psi,
            root,
            "EnvWinsApp/EnvWinsApp.csproj",
            "https");

        Assert.Equal("https://localhost:7001", psi.Environment["ASPNETCORE_URLS"]);
        Assert.Equal("https://localhost:7001", result.EffectiveUrls);
    }

    [Fact]
    public void ApplyTo_resolves_https_profile_when_configured_name_is_empty()
    {
        var psi = new ProcessStartInfo();
        var result = LaunchProfileEnvironmentApplier.ApplyTo(
            psi,
            root,
            "SampleApp/SampleApp.csproj",
            launchProfile: null);

        Assert.Equal("https", result.LaunchProfile);
        Assert.Equal("Development", result.EnvironmentName);
    }

    [Fact]
    public void ApplyTo_does_not_write_launchSettings_or_copy_another_project_environment()
    {
        var firstDir = Path.Combine(root, "FirstApp");
        var secondDir = Path.Combine(root, "SecondApp");
        Directory.CreateDirectory(Path.Combine(firstDir, "Properties"));
        Directory.CreateDirectory(Path.Combine(secondDir, "Properties"));
        WriteProfile(
            firstDir,
            """
            {
              "profiles": {
                "https": {
                  "commandName": "Project",
                  "environmentVariables": {
                    "ASPNETCORE_ENVIRONMENT": "FirstEnv",
                    "CUSTOM_FLAG": "from-first"
                  }
                }
              }
            }
            """);
        var secondPath = Path.Combine(secondDir, "Properties", "launchSettings.json");
        WriteProfile(
            secondDir,
            """
            {
              "profiles": {
                "https": {
                  "commandName": "Project",
                  "environmentVariables": {
                    "ASPNETCORE_ENVIRONMENT": "SecondEnv",
                    "CUSTOM_FLAG": "from-second"
                  }
                }
              }
            }
            """);
        var beforeWrite = File.GetLastWriteTimeUtc(secondPath);

        var first = new ProcessStartInfo();
        var second = new ProcessStartInfo();
        LaunchProfileEnvironmentApplier.ApplyTo(first, root, "FirstApp/FirstApp.csproj", "https");
        LaunchProfileEnvironmentApplier.ApplyTo(second, root, "SecondApp/SecondApp.csproj", "https");

        Assert.Equal("FirstEnv", first.Environment["ASPNETCORE_ENVIRONMENT"]);
        Assert.Equal("from-first", first.Environment["CUSTOM_FLAG"]);
        Assert.Equal("SecondEnv", second.Environment["ASPNETCORE_ENVIRONMENT"]);
        Assert.Equal("from-second", second.Environment["CUSTOM_FLAG"]);
        Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(secondPath));
        Assert.DoesNotContain(first.Environment.Values.Cast<string>(), v => v == "from-second");
    }

    [Fact]
    public void FormatStartDiagnostics_includes_identity_without_environment_values()
    {
        var line = LaunchProfileEnvironmentApplier.FormatStartDiagnostics(
            "2nd App",
            @"C:\src\App-worktree",
            @"C:\src\App-worktree\App.csproj",
            "https",
            "Development",
            "https://localhost:44349");

        Assert.Contains("2nd App", line, StringComparison.Ordinal);
        Assert.Contains(@"C:\src\App-worktree", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("App.csproj", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("profile=https", line, StringComparison.Ordinal);
        Assert.Contains("environment=Development", line, StringComparison.Ordinal);
        Assert.Contains("https://localhost:44349", line, StringComparison.Ordinal);
        Assert.DoesNotContain("CUSTOM_FLAG", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection", line, StringComparison.OrdinalIgnoreCase);
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

    private static void WriteProfile(string projectDir, string json) =>
        File.WriteAllText(Path.Combine(projectDir, "Properties", "launchSettings.json"), json);
}
