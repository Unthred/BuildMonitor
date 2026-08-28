using System.Text.Json;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class AzurePullRequestMetadataTests
{
    [Fact]
    public void Ordinary_branch_with_digits_is_not_a_pr()
    {
        Assert.Null(AzurePullRequestMetadata.TryResolveNumber(
            reason: "individualCI",
            sourceBranch: "refs/heads/feature/327-fix-widget",
            triggerInfo: null));
    }

    [Fact]
    public void Pull_ref_yields_number_when_pullRequest_reason()
    {
        Assert.Equal(327, AzurePullRequestMetadata.TryResolveNumber(
            "pullRequest",
            "refs/pull/327/merge",
            null));
    }

    [Fact]
    public void Malformed_pull_ref_yields_null()
    {
        Assert.Null(AzurePullRequestMetadata.TryResolveNumber(
            "pullRequest",
            "refs/pull/not-a-number/merge",
            null));
    }

    [Fact]
    public void TriggerInfo_pr_number_wins()
    {
        using var doc = JsonDocument.Parse("""{"pr.number":"441","pr.sourceBranch":"refs/heads/feature/foo"}""");
        Assert.Equal(441, AzurePullRequestMetadata.TryResolveNumber(
            "pullRequest",
            "refs/pull/441/merge",
            doc.RootElement));
    }

    [Fact]
    public void Display_branch_prefers_trigger_source_over_pull_ref()
    {
        using var doc = JsonDocument.Parse("""{"pr.number":"327","pr.sourceBranch":"refs/heads/feature/foo"}""");
        var display = AzurePullRequestMetadata.ResolveDisplayBranch(
            "refs/pull/327/merge",
            327,
            doc.RootElement);
        Assert.Equal("feature/foo", display);
    }

    [Fact]
    public void Display_branch_falls_back_to_PR_label()
    {
        var display = AzurePullRequestMetadata.ResolveDisplayBranch(
            "refs/pull/327/merge",
            327,
            null);
        Assert.Equal("PR #327", display);
    }

    [Fact]
    public void ResolveSourceBranchRef_returns_null_for_merge_ref_without_trigger()
    {
        Assert.Null(AzurePullRequestMetadata.ResolveSourceBranchRef("refs/pull/327/merge", null));
    }

    [Fact]
    public void ResolveSourceBranchRef_prefers_trigger_source_branch()
    {
        using var doc = JsonDocument.Parse("""{"pr.sourceBranch":"refs/heads/feature/foo"}""");
        Assert.Equal(
            "refs/heads/feature/foo",
            AzurePullRequestMetadata.ResolveSourceBranchRef("refs/pull/327/merge", doc.RootElement));
    }
}
