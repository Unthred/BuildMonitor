using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Infrastructure.Navigation;

/// <summary>
/// Resolves per-project browser preference and launches http(s) URIs.
/// Also implements <see cref="IBuildSourceLinkOpener"/> for Azure semantic navigation (#97).
/// </summary>
public sealed class ProjectLinkLauncher : IProjectLinkLauncher, IBuildSourceLinkOpener
{
    private readonly Func<AppSettings> getSettings;
    private readonly IRegisteredBrowserCatalog browserCatalog;
    private readonly IHttpUriLauncher httpUriLauncher;
    private readonly IAzureFailureNavigationResolver failureResolver;
    private readonly IAzureBranchNavigationResolver branchResolver;

    public ProjectLinkLauncher(
        Func<AppSettings> getSettings,
        IRegisteredBrowserCatalog browserCatalog,
        IHttpUriLauncher httpUriLauncher,
        IAzureFailureNavigationResolver failureResolver,
        IAzureBranchNavigationResolver branchResolver)
    {
        this.getSettings = getSettings;
        this.browserCatalog = browserCatalog;
        this.httpUriLauncher = httpUriLauncher;
        this.failureResolver = failureResolver;
        this.branchResolver = branchResolver;
    }

    public void OpenHttpUri(string projectId, Uri uri) => LaunchForProject(projectId, uri);

    public void OpenUri(string projectId, Uri uri) => LaunchForProject(projectId, uri);

    public async Task OpenFailureDetailsAsync(
        AzureBuildFailureNavigationRequest request,
        CancellationToken cancellationToken = default)
    {
        var destination = await failureResolver.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
        LaunchForProject(request.ProjectId, destination);
    }

    public async Task OpenBranchAsync(
        AzureBuildBranchNavigationRequest request,
        CancellationToken cancellationToken = default)
    {
        var destination = await branchResolver.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
        LaunchForProject(request.ProjectId, destination);
    }

    private void LaunchForProject(string projectId, Uri uri)
    {
        if (!HttpUriNavigationValidator.IsAllowedNavigationUri(uri))
        {
            return;
        }

        var settings = getSettings();
        var registeredId = ProjectLinkBrowserPreferenceRules.ResolveRegisteredBrowserId(settings, projectId);
        RegisteredBrowserDescriptor? browser = null;
        if (!string.IsNullOrWhiteSpace(registeredId)
            && !browserCatalog.TryResolve(registeredId, out browser))
        {
            browser = null;
        }

        httpUriLauncher.TryLaunch(uri, browser);
    }
}
