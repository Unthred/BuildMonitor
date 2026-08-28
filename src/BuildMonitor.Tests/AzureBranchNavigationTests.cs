using System.Net;
using System.Text;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.AzureDevOps;
using BuildMonitor.Infrastructure.Navigation;

namespace BuildMonitor.Tests;

public sealed class AzureBranchNavigationPolicyTests
{
    private const string BranchUrl =
        "https://dev.azure.com/org/project/_git/repo?version=GBfeature%2Fdeleted";
    private const string RunUrl =
        "https://dev.azure.com/org/project/_build/results?buildId=505&view=results";
    private const string PrUrl =
        "https://dev.azure.com/org/project/_git/repo/pullrequest/190";
    private const string CommitUrl =
        "https://dev.azure.com/org/project/_git/repo/commit/691538acd954126677fabf666d8af886ad094a27";

    [Fact]
    public void Existing_branch_uses_branch_url()
    {
        var uri = AzureBranchNavigationPolicy.SelectDestination(
            AzureBranchRefExistence.Exists,
            BranchUrl,
            RunUrl,
            PrUrl,
            CommitUrl);

        Assert.Equal(BranchUrl, uri.AbsoluteUri);
    }

    [Fact]
    public void Unknown_existence_still_uses_branch_url()
    {
        var uri = AzureBranchNavigationPolicy.SelectDestination(
            AzureBranchRefExistence.Unknown,
            BranchUrl,
            RunUrl,
            PrUrl,
            CommitUrl);

        Assert.Equal(BranchUrl, uri.AbsoluteUri);
    }

    [Fact]
    public void Deleted_branch_with_trustworthy_pr_uses_pr_url()
    {
        var uri = AzureBranchNavigationPolicy.SelectDestination(
            AzureBranchRefExistence.Deleted,
            BranchUrl,
            RunUrl,
            PrUrl,
            CommitUrl);

        Assert.Equal(PrUrl, uri.AbsoluteUri);
    }

    [Fact]
    public void Deleted_branch_without_pr_uses_commit_url()
    {
        var uri = AzureBranchNavigationPolicy.SelectDestination(
            AzureBranchRefExistence.Deleted,
            BranchUrl,
            RunUrl,
            pullRequestUrl: null,
            CommitUrl);

        Assert.Equal(CommitUrl, uri.AbsoluteUri);
    }

    [Fact]
    public void Deleted_branch_without_useful_fallback_uses_build_results()
    {
        var uri = AzureBranchNavigationPolicy.SelectDestination(
            AzureBranchRefExistence.Deleted,
            BranchUrl,
            RunUrl,
            pullRequestUrl: null,
            commitUrl: null);

        Assert.Equal(RunUrl, uri.AbsoluteUri);
    }

    [Fact]
    public void Cache_policy_treats_deleted_as_long_lived()
    {
        Assert.True(AzureBranchNavigationPolicy.ShouldCacheOutcome(AzureBranchRefExistence.Deleted));
        Assert.True(AzureBranchNavigationPolicy.IsLongLivedCache(AzureBranchRefExistence.Deleted));
        Assert.False(AzureBranchNavigationPolicy.IsLongLivedCache(AzureBranchRefExistence.Exists));
        Assert.False(AzureBranchNavigationPolicy.ShouldCacheOutcome(AzureBranchRefExistence.Unknown));
    }
}

public sealed class AzureGitCommitIdValidatorTests
{
    [Theory]
    [InlineData("691538acd954126677fabf666d8af886ad094a27", true)]
    [InlineData("691538ACD954126677FABF666D8AF886AD094A27", true)]
    [InlineData("not-a-commit", false)]
    [InlineData("", false)]
    [InlineData("abc", false)]
    public void Validates_full_sha_only(string value, bool expected) =>
        Assert.Equal(expected, AzureGitCommitIdValidator.IsValidCommitId(value));

    [Fact]
    public void Normalize_lowercases_valid_sha()
    {
        Assert.Equal(
            "691538acd954126677fabf666d8af886ad094a27",
            AzureGitCommitIdValidator.Normalize("691538ACD954126677FABF666D8AF886AD094A27"));
    }
}

public sealed class AzureBranchNavigationResolverTests
{
    private const string Org = "https://dev.azure.com/witherbyDev";
    private const string Project = "8b784aaf-7e28-4aeb-9b34-9734af8ca06b";
    private const string RepoId = "repo-guid";
    private const string RepoName = "WitherbyConnect";
    private const string BranchRef = "refs/heads/feature/AB-407-sendgrid-investigation-record";
    private const string BranchShort = "feature/AB-407-sendgrid-investigation-record";
    private const string Sha = "691538acd954126677fabf666d8af886ad094a27";

    private static readonly string BranchUrl =
        AzureDevOpsDeepLinkBuilder.BuildBranchUrl(Org, Project, RepoName, BranchShort);

    [Fact]
    public async Task Existing_branch_returns_branch_url()
    {
        var git = new RecordingGitRefClient(exists: true);
        var sut = CreateResolver(git);

        var destination = await sut.ResolveAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal(BranchUrl, destination.AbsoluteUri);
        Assert.Equal(1, git.CallCount);
    }

    [Fact]
    public async Task Deleted_branch_with_pr_returns_pr_url()
    {
        var git = new RecordingGitRefClient(exists: false);
        var sut = CreateResolver(git);
        var request = SampleRequest(pullRequestNumber: 190);

        var destination = await sut.ResolveAsync(request, CancellationToken.None);

        Assert.Equal(
            AzureDevOpsDeepLinkBuilder.BuildPullRequestUrl(Org, Project, RepoName, 190),
            destination.AbsoluteUri);
    }

    [Fact]
    public async Task Deleted_branch_without_pr_returns_commit_url()
    {
        var git = new RecordingGitRefClient(exists: false);
        var sut = CreateResolver(git);

        var destination = await sut.ResolveAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal(
            AzureDevOpsDeepLinkBuilder.BuildCommitUrl(Org, Project, RepoName, Sha),
            destination.AbsoluteUri);
    }

    [Fact]
    public async Task Deleted_branch_without_fallback_returns_build_results()
    {
        var git = new RecordingGitRefClient(exists: false);
        var sut = CreateResolver(git);
        var request = SampleRequest(sourceVersion: null);

        var destination = await sut.ResolveAsync(request, CancellationToken.None);

        Assert.Equal(
            AzureDevOpsDeepLinkBuilder.BuildRunResultsUrl(Org, Project, 505),
            destination.AbsoluteUri);
    }

    [Fact]
    public async Task Network_failure_does_not_treat_branch_as_deleted()
    {
        var git = new RecordingGitRefClient(outcome: AzureGitRefOutcome.Unavailable);
        var sut = CreateResolver(git);

        var destination = await sut.ResolveAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal(BranchUrl, destination.AbsoluteUri);
        Assert.False(sut.TryGetCached(SampleRequest(), out _));
    }

    [Fact]
    public async Task Confirmed_deleted_branch_is_cached()
    {
        var git = new RecordingGitRefClient(exists: false);
        var sut = CreateResolver(git);
        var request = SampleRequest();

        await sut.ResolveAsync(request, CancellationToken.None);
        await sut.ResolveAsync(request, CancellationToken.None);

        Assert.Equal(1, git.CallCount);
        Assert.True(sut.TryGetCached(request, out var cached));
        Assert.Equal(
            AzureDevOpsDeepLinkBuilder.BuildCommitUrl(Org, Project, RepoName, Sha),
            cached!.AbsoluteUri);
    }

    [Fact]
    public async Task Existing_branch_cache_avoids_repeat_ref_lookup_within_ttl()
    {
        var git = new RecordingGitRefClient(exists: true);
        var sut = CreateResolver(git);
        var request = SampleRequest();

        await sut.ResolveAsync(request, CancellationToken.None);
        await sut.ResolveAsync(request, CancellationToken.None);

        Assert.Equal(1, git.CallCount);
    }

    [Fact]
    public void Branch_request_uses_only_build_pr_identity_not_inference()
    {
        var run = new AzurePipelineRunInfo(
            8,
            "Pipe",
            505,
            "20260828.17",
            PipelineRunState.Completed,
            PipelineRunResult.Succeeded,
            BranchShort,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            AzureDevOpsDeepLinkBuilder.BuildRunResultsUrl(Org, Project, 505),
            PullRequestNumber: null,
            SourceBranchRef: BranchRef,
            SourceVersion: Sha);

        var nav = AzureBuildSourceNavigationBuilder.Build(
            run,
            new AzureBuildNavigationContext("p1", "conn", Org, Project, RepoName, RepoId));

        Assert.NotNull(nav.BranchRequest);
        Assert.Null(nav.BranchRequest!.PullRequestNumber);
    }

    [Fact]
    public void Invalid_sourceVersion_is_not_used_for_commit_navigation()
    {
        var run = new AzurePipelineRunInfo(
            8,
            "Pipe",
            505,
            "20260828.17",
            PipelineRunState.Completed,
            PipelineRunResult.Succeeded,
            BranchShort,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            AzureDevOpsDeepLinkBuilder.BuildRunResultsUrl(Org, Project, 505),
            SourceBranchRef: BranchRef,
            SourceVersion: "not-a-commit");

        var nav = AzureBuildSourceNavigationBuilder.Build(
            run,
            new AzureBuildNavigationContext("p1", "conn", Org, Project, RepoName, RepoId));

        Assert.NotNull(nav.BranchRequest);
        Assert.Null(nav.BranchRequest!.SourceVersion);
    }

    private static AzureBranchNavigationResolver CreateResolver(RecordingGitRefClient git) =>
        new(git, new FixedSecretStore("pat-token"));

    private static AzureBuildBranchNavigationRequest SampleRequest(
        int? pullRequestNumber = null,
        string? sourceVersion = Sha) =>
        new(
            ProjectId: "p1",
            ConnectionId: "conn-1",
            OrganizationUrl: Org,
            AdoProjectIdOrName: Project,
            RepositoryId: RepoId,
            RepositoryName: RepoName,
            RunId: 505,
            SourceBranchRef: BranchRef,
            SourceVersion: sourceVersion,
            PullRequestNumber: pullRequestNumber,
            BranchUrlFallback: BranchUrl);

    private sealed class RecordingGitRefClient : IAzureGitRefClient
    {
        private readonly AzureGitRefOutcome outcome;
        private readonly bool exists;

        public RecordingGitRefClient(bool exists)
        {
            this.exists = exists;
            outcome = AzureGitRefOutcome.Ok;
        }

        public RecordingGitRefClient(AzureGitRefOutcome outcome)
        {
            this.outcome = outcome;
            exists = false;
        }

        public int CallCount { get; private set; }

        public Task<AzureGitRefLookupResult> BranchRefExistsAsync(
            string organizationUrl,
            string adoProjectIdOrName,
            string repositoryId,
            string branchShortName,
            string? pat,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new AzureGitRefLookupResult(outcome, exists));
        }
    }

    private sealed class FixedSecretStore(string pat) : IAzureConnectionSecretStore
    {
        public Task<string?> LoadAsync(string connectionId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(pat);

        public Task SaveAsync(string connectionId, string value, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(string connectionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> ExistsAsync(string connectionId, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}

public sealed class AzureGitRefClientTests
{
    [Fact]
    public async Task Ref_lookup_uses_filter_and_auth_header_not_uri()
    {
        string? capturedUrl = null;
        string? capturedAuthScheme = null;
        string? capturedAuthParameter = null;
        using var handler = new CapturingHandler((request, _) =>
        {
            capturedUrl = request.RequestUri?.AbsoluteUri;
            capturedAuthScheme = request.Headers.Authorization?.Scheme;
            capturedAuthParameter = request.Headers.Authorization?.Parameter;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"count":1,"value":[{"name":"refs/heads/feature/foo"}]}""", Encoding.UTF8, "application/json")
            };
        });
        using var http = new HttpClient(handler);
        using var sut = new AzureGitRefClient(http);

        var result = await sut.BranchRefExistsAsync(
            "https://dev.azure.com/org",
            "project id",
            "repo-guid",
            "feature/foo",
            "secret-pat",
            CancellationToken.None);

        Assert.True(result.Exists);
        Assert.NotNull(capturedUrl);
        Assert.Contains("filter=heads%2Ffeature%2Ffoo", capturedUrl!, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-pat", capturedUrl!, StringComparison.Ordinal);
        Assert.Equal("Basic", capturedAuthScheme);
        Assert.NotNull(capturedAuthParameter);
        Assert.DoesNotContain("secret-pat", capturedAuthParameter!, StringComparison.Ordinal);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(capturedAuthParameter!));
        Assert.Equal(":secret-pat", decoded);
    }

    [Fact]
    public async Task Missing_ref_returns_not_exists()
    {
        using var handler = new CapturingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"count":0,"value":[]}""", Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        using var sut = new AzureGitRefClient(http);

        var result = await sut.BranchRefExistsAsync(
            "https://dev.azure.com/org",
            "project",
            "repo-guid",
            "deleted-branch",
            "pat",
            CancellationToken.None);

        Assert.Equal(AzureGitRefOutcome.Ok, result.Outcome);
        Assert.False(result.Exists);
    }

    [Fact]
    public async Task Unauthorized_maps_to_auth_required()
    {
        using var handler = new CapturingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = new HttpClient(handler);
        using var sut = new AzureGitRefClient(http);

        var result = await sut.BranchRefExistsAsync(
            "https://dev.azure.com/org",
            "project",
            "repo-guid",
            "master",
            "pat",
            CancellationToken.None);

        Assert.Equal(AzureGitRefOutcome.AuthRequired, result.Outcome);
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request, cancellationToken));
    }
}

public sealed class AzureBranchProjectLinkLauncherTests
{
    [Fact]
    public async Task OpenBranchAsync_uses_project_browser_for_resolved_destination()
    {
        var launcher = new RecordingHttpUriLauncher();
        var commit = new Uri("https://dev.azure.com/org/project/_git/repo/commit/691538acd954126677fabf666d8af886ad094a27");
        var settings = new AppSettings
        {
            SchemaVersion = SettingsSchemaV22.Version,
            Projects =
            [
                new MonitoredProjectSettings
                {
                    Id = "project-a",
                    DisplayName = "Project A",
                    LinkBrowserRegisteredId = "MSEdgeHTM"
                }
            ]
        };

        var sut = new ProjectLinkLauncher(
            () => settings,
            new FakeBrowserCatalog(new RegisteredBrowserDescriptor("MSEdgeHTM", "Edge", @"C:\Edge\msedge.exe")),
            launcher,
            new NoOpFailureResolver(),
            new FixedBranchResolver(commit));

        var request = new AzureBuildBranchNavigationRequest(
            ProjectId: "project-a",
            ConnectionId: "conn",
            OrganizationUrl: "https://dev.azure.com/org",
            AdoProjectIdOrName: "project",
            RepositoryId: "repo-id",
            RepositoryName: "repo",
            RunId: 505,
            SourceBranchRef: "refs/heads/feature/deleted",
            SourceVersion: "691538acd954126677fabf666d8af886ad094a27",
            PullRequestNumber: null,
            BranchUrlFallback: "https://dev.azure.com/org/project/_git/repo?version=GBfeature%2Fdeleted");

        await sut.OpenBranchAsync(request);

        var call = Assert.Single(launcher.Calls);
        Assert.Equal(commit, call.Uri);
        Assert.Equal("MSEdgeHTM", call.Browser?.RegisteredBrowserId);
    }

    [Fact]
    public async Task OpenBranchAsync_rejects_non_http_destination()
    {
        var launcher = new RecordingHttpUriLauncher();
        var settings = new AppSettings
        {
            SchemaVersion = SettingsSchemaV22.Version,
            Projects = [new MonitoredProjectSettings { Id = "p1", DisplayName = "P1" }]
        };

        var sut = new ProjectLinkLauncher(
            () => settings,
            new FakeBrowserCatalog(),
            launcher,
            new NoOpFailureResolver(),
            new FixedBranchResolver(new Uri("file:///C:/temp/x")));

        var request = new AzureBuildBranchNavigationRequest(
            ProjectId: "p1",
            ConnectionId: "conn",
            OrganizationUrl: "https://dev.azure.com/org",
            AdoProjectIdOrName: "project",
            RepositoryId: "repo-id",
            RepositoryName: "repo",
            RunId: 1,
            SourceBranchRef: "refs/heads/master",
            SourceVersion: null,
            PullRequestNumber: null,
            BranchUrlFallback: "https://dev.azure.com/org/project/_git/repo?version=GBmaster");

        await sut.OpenBranchAsync(request);

        Assert.Empty(launcher.Calls);
    }

    private sealed class FakeBrowserCatalog : IRegisteredBrowserCatalog
    {
        private readonly RegisteredBrowserDescriptor? browser;

        public FakeBrowserCatalog(RegisteredBrowserDescriptor? browser = null) => this.browser = browser;

        public IReadOnlyList<RegisteredBrowserDescriptor> GetBrowsers() =>
            browser is null ? [] : [browser];

        public void Refresh()
        {
        }

        public bool TryResolve(string? registeredBrowserId, out RegisteredBrowserDescriptor? resolved)
        {
            resolved = browser;
            return browser is not null;
        }
    }

    private sealed class RecordingHttpUriLauncher : IHttpUriLauncher
    {
        public List<(Uri Uri, RegisteredBrowserDescriptor? Browser)> Calls { get; } = [];

        public bool TryLaunch(Uri uri, RegisteredBrowserDescriptor? browser)
        {
            Calls.Add((uri, browser));
            return true;
        }
    }

    private sealed class NoOpFailureResolver : IAzureFailureNavigationResolver
    {
        public Task<Uri> ResolveAsync(
            AzureBuildFailureNavigationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new Uri("https://dev.azure.com/unused"));

        public bool TryGetCached(AzureBuildFailureNavigationRequest request, out Uri? destination)
        {
            destination = null;
            return false;
        }
    }

    private sealed class FixedBranchResolver(Uri destination) : IAzureBranchNavigationResolver
    {
        public Task<Uri> ResolveAsync(
            AzureBuildBranchNavigationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(destination);

        public bool TryGetCached(AzureBuildBranchNavigationRequest request, out Uri? cached)
        {
            cached = null;
            return false;
        }
    }
}
