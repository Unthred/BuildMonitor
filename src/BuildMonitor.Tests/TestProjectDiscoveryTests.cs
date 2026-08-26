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
    public void LooksLikeTestsExecuted_false_for_vstest_missing_debug_dll_banner()
    {
        const string log = """
            Test run for C:\src\BuildMonitor\src\BuildMonitor.Tests\bin\Debug\net10.0\BuildMonitor.Tests.dll (.NETCoreApp,Version=v10.0)
            VSTest version 17.14.0 (x64)

            The test source file "C:\src\BuildMonitor\src\BuildMonitor.Tests\bin\Debug\net10.0\BuildMonitor.Tests.dll" provided was not found.
            """;

        Assert.False(DotNetTestOutputParser.LooksLikeTestsExecuted(log));
        Assert.True(DotNetTestOutputParser.LooksLikeMissingTestSource(log));
        Assert.True(DotNetTestOutputParser.LooksLikeNeedsFullBuildBeforeTest(log));
    }

    [Fact]
    public void LooksLikeNeedsFullBuildBeforeTest_true_for_missing_file_after_test_run_banner()
    {
        const string log = """
            Test run for C:\src\App\bin\Debug\net10.0\App.Tests.dll (.NETCoreApp,Version=v10.0)
            Could not find file 'C:\src\App\bin\Debug\net10.0\App.Tests.dll'.
            """;

        Assert.False(DotNetTestOutputParser.LooksLikeTestsExecuted(log));
        Assert.True(DotNetTestOutputParser.LooksLikeNeedsFullBuildBeforeTest(log));
    }

    [Fact]
    public void LooksLikeNeedsFullBuildBeforeTest_false_for_assertion_failure()
    {
        const string log = """
            Test run for C:\src\App\bin\Debug\net10.0\App.Tests.dll (.NETCoreApp,Version=v10.0)
            Starting test execution, please wait...
            A total of 1 test files matched the specified pattern.

              Failed App.Tests.MathTests.Adds [12 ms]
              Error Message:
               Assert.Equal() Failure
            Expected: 2
            Actual:   3

            Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 12 ms - App.Tests.dll (net10.0)
            """;

        Assert.True(DotNetTestOutputParser.LooksLikeTestsExecuted(log));
        Assert.False(DotNetTestOutputParser.LooksLikeNeedsFullBuildBeforeTest(log));
        Assert.False(TestRunPlanner.ShouldRetryWithFullBuild(usedNoBuild: true, log));
    }

    [Fact]
    public void LooksLikeNeedsFullBuildBeforeTest_false_for_successful_no_build_run()
    {
        const string log = """
            Test run for C:\src\App\bin\Debug\net10.0\App.Tests.dll (.NETCoreApp,Version=v10.0)
            Starting test execution, please wait...
            A total of 1 test files matched the specified pattern.

              Passed App.Tests.MathTests.Adds [5 ms]

            Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 5 ms - App.Tests.dll (net10.0)
            """;

        Assert.True(DotNetTestOutputParser.LooksLikeTestsExecuted(log));
        Assert.False(DotNetTestOutputParser.LooksLikeNeedsFullBuildBeforeTest(log));
        Assert.False(TestRunPlanner.ShouldRetryWithFullBuild(usedNoBuild: true, log));
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
