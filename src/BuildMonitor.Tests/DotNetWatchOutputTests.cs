using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public class DotNetWatchOutputTests
{
    [Theory]
    [InlineData("Build FAILED.")]
    [InlineData("    Build FAILED")]
    [InlineData("dotnet watch ❌ C:\\src\\App\\App.csproj failed with 1 error(s) (0.5s)")]
    public void IsBuildFailedLine_matches_msbuild_and_watch_failures(string line)
    {
        Assert.True(DotNetWatchOutput.IsBuildFailedLine(line));
    }

    [Theory]
    [InlineData("The build failed validation for user input.")]
    [InlineData("Build failed to start because port 5000 is in use.")]
    [InlineData("Unhandled exception: something went wrong")]
    [InlineData("dotnet watch 🔥 Hot reload enabled.")]
    [InlineData("dotnet watch 🔨 Build succeeded: C:\\src\\App\\App.csproj")]
    [InlineData("dotnet watch ❌ [App (net9.0)] Change failed to apply (error code: '00-03-00-00-00'). Further changes won't be applied to this process.")]
    [InlineData("dotnet watch 🔥 Previous changes failed to apply. Further changes are not applied to this process.")]
    [InlineData("dotnet watch ⌚ Failed to receive response from a connected browser.")]
    public void IsBuildFailedLine_ignores_runtime_and_app_log_lines(string line)
    {
        Assert.False(DotNetWatchOutput.IsBuildFailedLine(line));
    }

    [Theory]
    [InlineData("The build failed. Please fix the build errors and run again.")]
    public void IsBuildFailedLine_matches_classic_watch_compile_failure(string line)
    {
        Assert.True(DotNetWatchOutput.IsBuildFailedLine(line));
    }
}
