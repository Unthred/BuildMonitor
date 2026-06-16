using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class HotReloadRestartDetectorTests
{
    [Theory]
    [InlineData("ENC0003: Updating 'Program' requires restarting the application.", HotReloadRestartRequest.RestartApp)]
    [InlineData("dotnet watch ⌚ Unable to apply hot reload because of a rude edit.", HotReloadRestartRequest.RestartApp)]
    [InlineData("dotnet watch ❌ Change failed to apply (error code: '00-01-00-00-00').", HotReloadRestartRequest.RestartApp)]
    [InlineData("Changes made require a rebuild of the application.", HotReloadRestartRequest.RebuildAndRestart)]
    public void Classify_detects_restart_and_rebuild_messages(string line, HotReloadRestartRequest expected) =>
        Assert.Equal(expected, HotReloadRestartDetector.Classify(line));

    [Theory]
    [InlineData("dotnet watch 🔥 Hot reload enabled.")]
    [InlineData("dotnet watch 🔥 Hot reload of static files succeeded.")]
    [InlineData("dotnet watch ⌚ Restarting application...")]
    public void Classify_ignores_informational_watch_lines(string line) =>
        Assert.Equal(HotReloadRestartRequest.None, HotReloadRestartDetector.Classify(line));

    [Fact]
    public void IsWatchAutoRestartMessage_matches_rude_edit_prompt_family() =>
        Assert.True(HotReloadRestartDetector.IsWatchAutoRestartMessage(
            "dotnet watch ⌚ Unable to apply hot reload because of a rude edit."));
}
