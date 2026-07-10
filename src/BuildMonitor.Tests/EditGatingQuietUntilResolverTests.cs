using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class EditGatingQuietUntilResolverTests
{
    [Fact]
    public void Resolve_uses_file_change_quiet_when_pending_rebuild()
    {
        var now = DateTimeOffset.UtcNow;
        var lastSave = now.AddSeconds(-1);
        var activity = EditActivitySnapshot.Inactive;

        var quietUntil = EditGatingQuietUntilResolver.Resolve(
            pendingFileChangeRebuild: true,
            lastMeaningfulFileChangeUtc: lastSave,
            debounceMs: 3000,
            activity);

        Assert.Equal(lastSave.AddMilliseconds(3000), quietUntil);
    }

    [Fact]
    public void Resolve_merges_agent_activity_when_pending_rebuild()
    {
        var now = DateTimeOffset.UtcNow;
        var lastSave = now.AddSeconds(-4);
        var agentQuietUntil = now.AddSeconds(8);
        var activity = new EditActivitySnapshot(true, agentQuietUntil, "agent tooling activity");

        var quietUntil = EditGatingQuietUntilResolver.Resolve(
            pendingFileChangeRebuild: true,
            lastMeaningfulFileChangeUtc: lastSave,
            debounceMs: 3000,
            activity);

        Assert.Equal(agentQuietUntil, quietUntil);
    }

    [Fact]
    public void Resolve_uses_agent_activity_when_not_pending_rebuild()
    {
        var now = DateTimeOffset.UtcNow;
        var agentQuietUntil = now.AddSeconds(5);
        var activity = new EditActivitySnapshot(true, agentQuietUntil, "agent tooling activity");

        var quietUntil = EditGatingQuietUntilResolver.Resolve(
            pendingFileChangeRebuild: false,
            lastMeaningfulFileChangeUtc: DateTimeOffset.MinValue,
            debounceMs: 3000,
            activity);

        Assert.Equal(agentQuietUntil, quietUntil);
    }

    [Fact]
    public void Resolve_returns_null_when_no_gating()
    {
        var quietUntil = EditGatingQuietUntilResolver.Resolve(
            pendingFileChangeRebuild: false,
            lastMeaningfulFileChangeUtc: DateTimeOffset.MinValue,
            debounceMs: 3000,
            EditActivitySnapshot.Inactive);

        Assert.Null(quietUntil);
    }
}
