using System.Diagnostics;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.AzureDevOps;

/// <summary>Default browser launcher for BUILDS navigation (future #96 seam).</summary>
public sealed class BuildSourceLinkOpener : IBuildSourceLinkOpener
{
    private readonly IAzureFailureNavigationResolver failureResolver;

    public BuildSourceLinkOpener(IAzureFailureNavigationResolver failureResolver)
    {
        this.failureResolver = failureResolver;
    }

    public void OpenUri(Uri uri) => OpenHttpNavigationUri(uri);

    public async Task OpenFailureDetailsAsync(
        AzureBuildFailureNavigationRequest request,
        CancellationToken cancellationToken = default)
    {
        var destination = await failureResolver.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
        OpenHttpNavigationUri(destination);
    }

    /// <summary>Central browser launch seam for project link navigation.</summary>
    public static void OpenHttpNavigationUri(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // ignore launch failures
        }
    }
}
