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

    [Fact]
    public void ResolveSourceBranchRef_uses_parameters_when_trigger_lacks_source_branch()
    {
        const string parameters = """
            {"system.pullRequest.sourceBranch":"refs/heads/feature/AB-408-dataset-xml-security"}
            """;
        Assert.Equal(
            "refs/heads/feature/AB-408-dataset-xml-security",
            AzurePullRequestMetadata.ResolveSourceBranchRef(
                "refs/pull/188/merge",
                triggerInfo: null,
                buildParametersJson: parameters));
    }

    [Fact]
    public void ResolveSourceBranchRef_prefers_trigger_over_parameters()
    {
        using var doc = JsonDocument.Parse("""{"pr.sourceBranch":"refs/heads/from-trigger"}""");
        const string parameters = """
            {"system.pullRequest.sourceBranch":"refs/heads/from-parameters"}
            """;
        Assert.Equal(
            "refs/heads/from-trigger",
            AzurePullRequestMetadata.ResolveSourceBranchRef(
                "refs/pull/327/merge",
                doc.RootElement,
                parameters));
    }

    [Fact]
    public void ResolveSourceBranchRef_rejects_parameters_merge_ref()
    {
        const string parameters = """
            {"system.pullRequest.sourceBranch":"refs/pull/327/merge"}
            """;
        Assert.Null(AzurePullRequestMetadata.ResolveSourceBranchRef(
            "refs/pull/327/merge",
            null,
            parameters));
    }

    [Fact]
    public void ResolveSourceBranchRef_rejects_malformed_parameters_json()
    {
        Assert.Null(AzurePullRequestMetadata.TryParametersSourceBranch("{not-json"));
    }

    [Fact]
    public void ResolveSourceBranchRef_rejects_bare_branch_name_in_parameters()
    {
        const string parameters = """{"system.pullRequest.sourceBranch":"feature/foo"}""";
        Assert.Null(AzurePullRequestMetadata.TryParametersSourceBranch(parameters));
    }

    [Fact]
    public void Display_branch_uses_parameters_source_when_trigger_lacks_branch()
    {
        const string parameters = """
            {"system.pullRequest.sourceBranch":"refs/heads/feature/AB-408-dataset-xml-security"}
            """;
        var display = AzurePullRequestMetadata.ResolveDisplayBranch(
            "refs/pull/188/merge",
            188,
            triggerInfo: null,
            buildParametersJson: parameters);
        Assert.Equal("feature/AB-408-dataset-xml-security", display);
    }

    [Fact]
    public void Non_pr_branch_unchanged_without_parameters()
    {
        Assert.Equal(
            "refs/heads/master",
            AzurePullRequestMetadata.ResolveSourceBranchRef("refs/heads/master", null));
    }

    [Fact]
    public void Non_pr_branch_ignores_parameters()
    {
        const string parameters = """
            {"system.pullRequest.sourceBranch":"refs/heads/should-not-win"}
            """;
        Assert.Equal(
            "refs/heads/master",
            AzurePullRequestMetadata.ResolveSourceBranchRef(
                "refs/heads/master",
                null,
                parameters));
    }
}
