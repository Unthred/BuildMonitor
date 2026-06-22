using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class BuildLifecycleFormattingTests
{
    [Theory]
    [InlineData(ProjectLifecycleState.BuildOk, true)]
    [InlineData(ProjectLifecycleState.Watching, true)]
    [InlineData(ProjectLifecycleState.Running, true)]
    [InlineData(ProjectLifecycleState.BuildFailed, false)]
    public void IsSuccessfulBuildEndState_recognises_success_paths(
        ProjectLifecycleState state,
        bool expected)
    {
        Assert.Equal(expected, BuildLifecycleFormatting.IsSuccessfulBuildEndState(state));
    }

    [Fact]
    public void FormatBuildDuration_uses_seconds_for_short_runs()
    {
        Assert.Equal("3.5s", BuildLifecycleFormatting.FormatBuildDuration(TimeSpan.FromSeconds(3.5)));
    }
}
