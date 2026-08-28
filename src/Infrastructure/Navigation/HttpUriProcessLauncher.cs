using System.Diagnostics;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Infrastructure.Navigation;

/// <summary>Safe http(s) process launcher (system default shell or explicit browser via ArgumentList).</summary>
public sealed class HttpUriProcessLauncher : IHttpUriLauncher
{
    public bool TryLaunch(Uri uri, RegisteredBrowserDescriptor? browser)
    {
        if (!HttpUriNavigationValidator.IsAllowedNavigationUri(uri))
        {
            return false;
        }

        try
        {
            if (browser is null)
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                return true;
            }

            if (string.IsNullOrWhiteSpace(browser.ExecutablePath) || !File.Exists(browser.ExecutablePath))
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                return true;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = browser.ExecutablePath,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(uri.AbsoluteUri);
            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
