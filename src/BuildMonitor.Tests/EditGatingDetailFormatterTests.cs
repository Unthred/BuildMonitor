using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class EditGatingDetailFormatterTests
{
    [Fact]
    public void FormatCountdownRemaining_shows_whole_seconds()
    {
        var now = DateTimeOffset.UtcNow;
        var text = EditGatingDetailFormatter.FormatCountdownRemaining(now.AddSeconds(12.2), now);

        Assert.Equal("Rebuild in 13 s", text);
    }

    [Fact]
    public void FormatCountdownRemaining_at_zero_shows_starting()
    {
        var now = DateTimeOffset.UtcNow;
        var text = EditGatingDetailFormatter.FormatCountdownRemaining(now, now);

        Assert.Equal("Rebuild starting…", text);
    }
}
