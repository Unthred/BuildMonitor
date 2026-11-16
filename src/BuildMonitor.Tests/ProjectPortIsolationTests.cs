using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class ProjectPortIsolationTests
{
    [Fact]
    public void Offset_of_16_moves_witherby_https_to_44349()
    {
        var shifted = ProjectPortIsolation.OffsetUrls(
            ["https://localhost:44333", "http://localhost:5154"],
            ProjectPortIsolation.SiblingPortOffset);

        Assert.Equal("https://localhost:44349", shifted[0]);
        Assert.Equal("http://localhost:5170", shifted[1]);
    }

    [Fact]
    public void Explicit_override_is_stable_when_free()
    {
        var decision = ProjectPortIsolation.Decide(
            "https://localhost:44349",
            ["https://localhost:44333"],
            peers: []);

        Assert.True(decision.CanStart);
        Assert.False(decision.PersistApplicationUrl);
        Assert.Equal("https://localhost:44349", decision.ApplicationUrl);
    }

    [Fact]
    public void Explicit_override_is_blocked_when_a_peer_owns_the_port()
    {
        var peer = new PeerProjectListenInfo(
            "peer-1",
            "App (main)",
            @"C:\src\App",
            ["https://localhost:44349"],
            IsRunProcessActive: true);

        var decision = ProjectPortIsolation.Decide(
            "https://localhost:44349",
            ["https://localhost:44333"],
            [peer]);

        Assert.False(decision.CanStart);
        Assert.Same(peer, decision.BlockingPeer);
        Assert.Equal(44349, decision.BlockingPort);
        Assert.Null(decision.ApplicationUrl);
    }

    [Fact]
    public void Unconfigured_profile_collision_persists_an_offset_url_on_this_project_only()
    {
        var peer = new PeerProjectListenInfo(
            "peer-1",
            "App (main)",
            @"C:\src\App",
            ["https://localhost:44333", "http://localhost:5154"],
            IsRunProcessActive: true);

        var decision = ProjectPortIsolation.Decide(
            configuredOverride: null,
            profileUrls: ["https://localhost:44333", "http://localhost:5154"],
            peers: [peer]);

        Assert.True(decision.CanStart);
        Assert.True(decision.PersistApplicationUrl);
        Assert.Equal("https://localhost:44349;http://localhost:5170", decision.ApplicationUrl);
    }

    [Fact]
    public void Unrelated_peer_ports_do_not_force_an_offset()
    {
        var peer = new PeerProjectListenInfo(
            "peer-2",
            "BuildMonitor.TrayApp",
            @"C:\src\Tools",
            ["https://localhost:7123"],
            IsRunProcessActive: true);

        var decision = ProjectPortIsolation.Decide(
            configuredOverride: null,
            profileUrls: ["https://localhost:44333"],
            peers: [peer]);

        Assert.True(decision.CanStart);
        Assert.False(decision.PersistApplicationUrl);
        Assert.Equal("https://localhost:44333", decision.ApplicationUrl);
    }

    private static PeerProjectListenInfo Peer(string id, params string[] urls) =>
        new(id, id, @"C:\src\" + id, urls, IsRunProcessActive: false);

    [Fact]
    public void AllocateNextAvailable_keeps_preferred_ports_when_free()
    {
        var decision = ProjectPortIsolation.AllocateNextAvailable(
            ["https://localhost:44333"], peers: []);

        Assert.True(decision.CanStart);
        Assert.False(decision.PersistApplicationUrl);
        Assert.Equal("https://localhost:44333", decision.ApplicationUrl);
    }

    [Fact]
    public void AllocateNextAvailable_hands_out_consecutive_ports_for_successive_worktrees()
    {
        var peers = new List<PeerProjectListenInfo>();
        var expected = new[] { "https://localhost:44333", "https://localhost:44334", "https://localhost:44335" };
        for (var i = 0; i < expected.Length; i++)
        {
            var decision = ProjectPortIsolation.AllocateNextAvailable(["https://localhost:44333"], peers);
            Assert.True(decision.CanStart);
            Assert.Equal(expected[i], decision.ApplicationUrl);
            peers.Add(Peer($"wt{i}", decision.ApplicationUrl!));
        }
    }

    [Fact]
    public void AllocateNextAvailable_skips_ports_claimed_on_any_url_of_the_set()
    {
        var decision = ProjectPortIsolation.AllocateNextAvailable(
            ["https://localhost:44333", "http://localhost:5154"],
            [Peer("a", "https://localhost:44333", "http://localhost:5154"), Peer("b", "http://localhost:5155")]);

        Assert.True(decision.CanStart);
        Assert.True(decision.PersistApplicationUrl);
        Assert.Equal("https://localhost:44335;http://localhost:5156", decision.ApplicationUrl);
    }

    [Fact]
    public void AllocateNextAvailable_blocks_only_when_the_pool_is_exhausted()
    {
        var urls = Enumerable.Range(0, ProjectPortIsolation.MaxSequentialAllocationSteps + 1)
            .Select(i => $"https://localhost:{44333 + i}")
            .ToArray();
        var decision = ProjectPortIsolation.AllocateNextAvailable(
            ["https://localhost:44333"], [Peer("full", urls)]);

        Assert.False(decision.CanStart);
        Assert.Equal(44333, decision.BlockingPort);
    }

    [Fact]
    public void AllocateNextAvailable_does_not_overflow_past_the_top_of_the_port_range()
    {
        var decision = ProjectPortIsolation.AllocateNextAvailable(
            ["https://localhost:65535"], [Peer("top", "https://localhost:65535")]);

        Assert.False(decision.CanStart);
    }
}
