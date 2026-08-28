using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using Microsoft.Win32;

namespace BuildMonitor.Infrastructure.Navigation;

/// <summary>
/// Enumerates HTTP browsers registered under Windows StartMenuInternet.
/// Does not execute registry command strings — extracts executable paths only.
/// </summary>
public static class WindowsRegisteredBrowserDiscovery
{
    private static readonly string[] RegistryPaths =
    [
        @"SOFTWARE\Clients\StartMenuInternet",
        @"SOFTWARE\WOW6432Node\Clients\StartMenuInternet"
    ];

    public static IReadOnlyList<RegisteredBrowserDescriptor> Discover()
    {
        var byId = new Dictionary<string, RegisteredBrowserDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var basePath in RegistryPaths)
            {
                CollectFromHive(Registry.LocalMachine, basePath, view, byId);
            }
        }

        CollectFromHive(Registry.CurrentUser, @"SOFTWARE\Clients\StartMenuInternet", RegistryView.Default, byId);

        return byId.Values
            .OrderBy(b => b.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CollectFromHive(
        RegistryKey hive,
        string basePath,
        RegistryView view,
        Dictionary<string, RegisteredBrowserDescriptor> byId)
    {
        using var baseKey = view == RegistryView.Default
            ? hive.OpenSubKey(basePath)
            : hive.OpenSubKey(basePath, writable: false);
        if (baseKey is null)
        {
            return;
        }

        foreach (var subKeyName in baseKey.GetSubKeyNames())
        {
            if (string.IsNullOrWhiteSpace(subKeyName))
            {
                continue;
            }

            using var browserKey = baseKey.OpenSubKey(subKeyName);
            if (browserKey is null)
            {
                continue;
            }

            var displayName = (browserKey.GetValue(null) as string)?.Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = subKeyName;
            }

            using var commandKey = browserKey.OpenSubKey(@"shell\open\command");
            var command = commandKey?.GetValue(null) as string;
            var executable = BrowserLaunchCommandParser.TryExtractExecutablePath(command);
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            {
                continue;
            }

            byId[subKeyName] = new RegisteredBrowserDescriptor(subKeyName, displayName, executable);
        }
    }
}
