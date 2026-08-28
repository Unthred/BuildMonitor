using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class StatusPanelActionLabelsTests
{
    [Fact]
    public void Rebuild_and_restart_label_matches_tray_semantics()
    {
        Assert.Equal("Rebuild & restart", StatusPanelActionLabels.RebuildAndRestart);
        Assert.Contains("Full build", StatusPanelActionLabels.RebuildAndRestartToolTip, StringComparison.OrdinalIgnoreCase);
    }
}
