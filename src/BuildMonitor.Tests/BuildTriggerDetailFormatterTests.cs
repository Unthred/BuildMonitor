using BuildMonitor.Core.Models;
using BuildMonitor.Infrastructure.Diagnostics;

namespace BuildMonitor.Tests;

public sealed class BuildTriggerDetailFormatterTests
{
    [Fact]
    public void FormatImmediateDebounce_includes_ms()
    {
        var detail = BuildTriggerDetailFormatter.FormatImmediateDebounce(3000);
        Assert.Contains("3000", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatCoalescedBuild_includes_hold_reason()
    {
        var detail = BuildTriggerDetailFormatter.FormatCoalescedBuild(
            4500,
            PendingRebuildHoldReason.BuildInProgress,
            timerResetCount: 0);

        Assert.Contains("4500", detail, StringComparison.Ordinal);
        Assert.Contains("build finished", detail, StringComparison.OrdinalIgnoreCase);
    }
}
