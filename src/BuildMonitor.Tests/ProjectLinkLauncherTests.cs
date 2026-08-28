using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Navigation;

namespace BuildMonitor.Tests;

public sealed class ProjectLinkLauncherTests
{
    [Fact]
    public void Launch_uses_system_default_when_preference_unset()
    {
        var launcher = new RecordingHttpUriLauncher();
        var catalog = new FakeBrowserCatalog();
        var settings = SampleSettings(projectA: null, projectB: "ChromeHTML", projectC: null);
        var sut = CreateSut(settings, catalog, launcher);

        sut.OpenUri("project-c", new Uri("https://example.com/default"));

        var call = Assert.Single(launcher.Calls);
        Assert.Null(call.Browser);
    }

    [Fact]
    public void Launch_uses_edge_for_project_a_and_chrome_for_project_b()
    {
        var launcher = new RecordingHttpUriLauncher();
        var catalog = new FakeBrowserCatalog(
            new RegisteredBrowserDescriptor("MSEdgeHTM", "Microsoft Edge", @"C:\Edge\msedge.exe"),
            new RegisteredBrowserDescriptor("ChromeHTML", "Google Chrome", @"C:\Chrome\chrome.exe"));
        var settings = SampleSettings(projectA: "MSEdgeHTM", projectB: "ChromeHTML", projectC: null);
        var sut = CreateSut(settings, catalog, launcher);

        sut.OpenUri("project-a", new Uri("https://example.com/a"));
        sut.OpenUri("project-b", new Uri("https://example.com/b"));

        Assert.Equal(2, launcher.Calls.Count);
        Assert.Equal("MSEdgeHTM", launcher.Calls[0].Browser?.RegisteredBrowserId);
        Assert.Equal("ChromeHTML", launcher.Calls[1].Browser?.RegisteredBrowserId);
    }

    [Fact]
    public void Launch_falls_back_to_system_default_when_configured_browser_missing()
    {
        var launcher = new RecordingHttpUriLauncher();
        var catalog = new FakeBrowserCatalog();
        var settings = SampleSettings(projectA: "MSEdgeHTM", projectB: null, projectC: null);
        var sut = CreateSut(settings, catalog, launcher);

        sut.OpenUri("project-a", new Uri("https://example.com/missing"));

        var call = Assert.Single(launcher.Calls);
        Assert.Null(call.Browser);
    }

    [Fact]
    public void Launch_does_not_mutate_persisted_preference_when_browser_missing()
    {
        var launcher = new RecordingHttpUriLauncher();
        var catalog = new FakeBrowserCatalog();
        var settings = SampleSettings(projectA: "MSEdgeHTM", projectB: null, projectC: null);
        var sut = CreateSut(settings, catalog, launcher);

        sut.OpenUri("project-a", new Uri("https://example.com/missing"));

        Assert.Equal("MSEdgeHTM", settings.Projects[0].LinkBrowserRegisteredId);
    }

    [Fact]
    public void Launch_rejects_non_http_uri()
    {
        var launcher = new RecordingHttpUriLauncher();
        var catalog = new FakeBrowserCatalog();
        var settings = SampleSettings(projectA: null, projectB: null, projectC: null);
        var sut = CreateSut(settings, catalog, launcher);

        sut.OpenUri("project-a", new Uri("file:///C:/temp/x.txt"));

        Assert.Empty(launcher.Calls);
    }

    private static ProjectLinkLauncher CreateSut(
        AppSettings settings,
        IRegisteredBrowserCatalog catalog,
        IHttpUriLauncher httpLauncher)
    {
        return new ProjectLinkLauncher(
            () => settings,
            catalog,
            httpLauncher,
            new NoOpFailureResolver());
    }

    private static AppSettings SampleSettings(string? projectA, string? projectB, string? projectC) =>
        new()
        {
            SchemaVersion = SettingsSchemaV22.Version,
            Projects =
            [
                new MonitoredProjectSettings
                {
                    Id = "project-a",
                    DisplayName = "Project A",
                    LinkBrowserRegisteredId = projectA
                },
                new MonitoredProjectSettings
                {
                    Id = "project-b",
                    DisplayName = "Project B",
                    LinkBrowserRegisteredId = projectB
                },
                new MonitoredProjectSettings
                {
                    Id = "project-c",
                    DisplayName = "Project C",
                    LinkBrowserRegisteredId = projectC
                }
            ]
        };

    private sealed class FakeBrowserCatalog : IRegisteredBrowserCatalog
    {
        private readonly IReadOnlyList<RegisteredBrowserDescriptor> browsers;

        public FakeBrowserCatalog(params RegisteredBrowserDescriptor[] browsers) =>
            this.browsers = browsers;

        public IReadOnlyList<RegisteredBrowserDescriptor> GetBrowsers() => browsers;

        public void Refresh()
        {
        }

        public bool TryResolve(string? registeredBrowserId, out RegisteredBrowserDescriptor? browser)
        {
            browser = browsers.FirstOrDefault(
                b => string.Equals(b.RegisteredBrowserId, registeredBrowserId, StringComparison.OrdinalIgnoreCase));
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
            Task.FromResult(new Uri("https://dev.azure.com/failure"));

        public bool TryGetCached(AzureBuildFailureNavigationRequest request, out Uri? destination)
        {
            destination = null;
            return false;
        }
    }
}
