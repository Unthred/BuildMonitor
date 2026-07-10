using BuildMonitor.Core.Rules;
using BuildMonitor.Infrastructure.Diagnostics;

namespace BuildMonitor.Tests;

public sealed class InFlightBuildUnexpectedNoteFormatterTests
{
    [Fact]
    public void Format_active_activity_includes_reason_and_quiet_remaining()
    {
        var now = DateTimeOffset.UtcNow;
        var activity = new EditActivitySnapshot(true, now.AddSeconds(8), "agent tooling activity");

        var note = InFlightBuildUnexpectedNoteFormatter.Format(activity, now);

        Assert.Contains("AI", note, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("agent tooling activity", note, StringComparison.Ordinal);
        Assert.Contains("quiet", note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Format_inactive_activity_notes_no_signal()
    {
        var note = InFlightBuildUnexpectedNoteFormatter.Format(
            EditActivitySnapshot.Inactive,
            DateTimeOffset.UtcNow);

        Assert.Contains("no active edit-gating signal", note, StringComparison.OrdinalIgnoreCase);
    }
}
