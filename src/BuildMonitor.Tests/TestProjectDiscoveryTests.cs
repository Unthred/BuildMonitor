using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public class TestProjectDiscoveryTests
{
    [Fact]
    public void IsTestProject_detects_test_sdk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csproj = Path.Combine(dir, "Sample.Tests.csproj");
        try
        {
            File.WriteAllText(csproj, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <IsTestProject>true</IsTestProject>
                  </PropertyGroup>
                </Project>
                """);

            Assert.True(TestProjectDiscovery.IsTestProject(csproj));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Resolve_prefers_solution_over_app_csproj()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var app = Path.Combine(dir, "MyApp.csproj");
        var sln = Path.Combine(dir, "MyApp.sln");
        var tests = Path.Combine(dir, "MyApp.Tests.csproj");
        try
        {
            File.WriteAllText(app, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(sln, "fake solution");
            File.WriteAllText(tests, "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"xunit\" /></ItemGroup></Project>");

            var resolution = TestProjectDiscovery.Resolve(dir, "MyApp.csproj", null);

            Assert.True(resolution.AutoDiscovered);
            Assert.Equal(sln, resolution.Targets[0]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LooksLikeTestsExecuted_false_for_restore_only()
    {
        const string log = """
            Done Building Project "App.csproj" (Restore target(s)).
            Build succeeded.
            """;

        Assert.False(DotNetTestOutputParser.LooksLikeTestsExecuted(log));
        Assert.True(DotNetTestOutputParser.LooksLikeRestoreOrBuildOnly(log));
    }

    [Fact]
    public void LooksLikeNeedsFullBuildBeforeTest_true_when_not_built()
    {
        const string log = """
            The project 'MyApp.Tests' has not been built. Run `dotnet build` to build the project.
            """;

        Assert.True(DotNetTestOutputParser.LooksLikeNeedsFullBuildBeforeTest(log));
        Assert.False(DotNetTestOutputParser.LooksLikeTestsExecuted(log));
    }

    [Fact]
    public void LooksLikeNeedsFullBuildBeforeTest_false_when_tests_ran()
    {
        const string log = "Failed!  - Failed: 1, Passed: 11, Skipped: 0, Total: 12, Duration: 1 s";

        Assert.False(DotNetTestOutputParser.LooksLikeNeedsFullBuildBeforeTest(log));
        Assert.True(DotNetTestOutputParser.LooksLikeTestsExecuted(log));
    }

    [Fact]
    public void IsOutputLockError_detects_msbuild_copy_failure()
    {
        const string log = """
            error MSB3027: Could not copy "App.exe" to "bin\Debug\net9.0\App.exe". Exceeded retry count of 10. Failed.
            error MSB3021: Unable to copy file because it is being used by another process.
            """;

        Assert.True(BuildLogParser.IsOutputLockError(log));
    }
}
