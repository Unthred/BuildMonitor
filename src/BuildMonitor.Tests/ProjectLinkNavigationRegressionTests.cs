using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Navigation;

namespace BuildMonitor.Tests;

/// <summary>
/// #97 semantic navigation must pass the same destination URIs; #96 only changes launch browser.
/// </summary>
public sealed class ProjectLinkNavigationRegressionTests
{
    [Theory]
    [InlineData("https://dev.azure.com/org/project/_build/results?buildId=42")]
    [InlineData("https://dev.azure.com/org/project/_git/repo/pullrequest/7")]
    [InlineData("https://dev.azure.com/org/project/_git/repo?version=GBfeature/x")]
    public void OpenUri_passes_exact_http_destination(string uriText)
    {
        var launcher = new RecordingHttpUriLauncher();
        var settings = SettingsWithBrowser("project-a", "ChromeHTML");
        var sut = CreateSut(settings, launcher);

        sut.OpenUri("project-a", new Uri(uriText));

        var call = Assert.Single(launcher.Calls);
        Assert.Equal(uriText, call.Uri.AbsoluteUri);
        Assert.Equal("ChromeHTML", call.Browser?.RegisteredBrowserId);
    }

    [Fact]
    public async Task OpenFailureDetailsAsync_passes_resolver_destination_unchanged()
    {
        var launcher = new RecordingHttpUriLauncher();
        var destination = new Uri("https://dev.azure.com/org/project/_build/results?buildId=99&view=logs&s=abc");
        var settings = SettingsWithBrowser("project-a", "MSEdgeHTM");
        var sut = new ProjectLinkLauncher(
            () => settings,
            new FakeBrowserCatalog(new RegisteredBrowserDescriptor("MSEdgeHTM", "Edge", @"C:\Edge\msedge.exe")),
            launcher,
            new FixedFailureResolver(destination),
            new NoOpBranchResolver());

        var request = new AzureBuildFailureNavigationRequest(
            ProjectId: "project-a",
            ConnectionId: "conn",
            OrganizationUrl: "https://dev.azure.com/org",
            AdoProjectIdOrName: "project",
            RunId: 99);

        await sut.OpenFailureDetailsAsync(request);

        var call = Assert.Single(launcher.Calls);
        Assert.Equal(destination, call.Uri);
        Assert.Equal("MSEdgeHTM", call.Browser?.RegisteredBrowserId);
    }

    private static ProjectLinkLauncher CreateSut(AppSettings settings, RecordingHttpUriLauncher launcher) =>
        new(
            () => settings,
            new FakeBrowserCatalog(new RegisteredBrowserDescriptor("ChromeHTML", "Chrome", @"C:\Chrome\chrome.exe")),
            launcher,
            new FixedFailureResolver(new Uri("https://dev.azure.com/unused")),
            new NoOpBranchResolver());

    private static AppSettings SettingsWithBrowser(string projectId, string browserId) =>
        new()
        {
            SchemaVersion = SettingsSchemaV22.Version,
            Projects =
            [
                new MonitoredProjectSettings
                {
                    Id = projectId,
                    DisplayName = "Project",
                    LinkBrowserRegisteredId = browserId
                }
            ]
        };

    private sealed class FakeBrowserCatalog : IRegisteredBrowserCatalog
    {
        private readonly RegisteredBrowserDescriptor browser;

        public FakeBrowserCatalog(RegisteredBrowserDescriptor browser) => this.browser = browser;

        public IReadOnlyList<RegisteredBrowserDescriptor> GetBrowsers() => [browser];

        public void Refresh()
        {
        }

        public bool TryResolve(string? registeredBrowserId, out RegisteredBrowserDescriptor? resolved)
        {
            resolved = string.Equals(registeredBrowserId, browser.RegisteredBrowserId, StringComparison.OrdinalIgnoreCase)
                ? browser
                : null;
            return resolved is not null;
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

    private sealed class FixedFailureResolver(Uri destination) : IAzureFailureNavigationResolver
    {
        public Task<Uri> ResolveAsync(
            AzureBuildFailureNavigationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(destination);

        public bool TryGetCached(AzureBuildFailureNavigationRequest request, out Uri? cached)
        {
            cached = null;
            return false;
        }
    }

    private sealed class NoOpBranchResolver : IAzureBranchNavigationResolver
    {
        public Task<Uri> ResolveAsync(
            AzureBuildBranchNavigationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new Uri(request.BranchUrlFallback));

        public bool TryGetCached(AzureBuildBranchNavigationRequest request, out Uri? destination)
        {
            destination = null;
            return false;
        }
    }
}
