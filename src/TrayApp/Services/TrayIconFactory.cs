using System.Drawing;
using System.Reflection;
using BuildMonitor.Core.Models;

namespace BuildMonitor.TrayApp.Services;

/// <summary>
/// Loads committed builder-duck tray icons (#95). Static assets only — no runtime drawing.
/// </summary>
public static class TrayIconFactory
{
    private static readonly Dictionary<TrayIconPresentationState, Icon> Cache = new();

    public static Icon GetIcon(TrayIconPresentationState state)
    {
        if (Cache.TryGetValue(state, out var cached))
        {
            return cached;
        }

        var fileName = state switch
        {
            TrayIconPresentationState.Healthy => "tray-healthy.ico",
            TrayIconPresentationState.Building => "tray-building.ico",
            TrayIconPresentationState.Attention => "tray-attention.ico",
            TrayIconPresentationState.Failed => "tray-failed.ico",
            _ => "tray-neutral.ico"
        };

        var resourceName = ResolveEmbeddedResourceName(fileName)
            ?? throw new InvalidOperationException($"Tray icon resource not found: {fileName}");

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Tray icon stream missing: {resourceName}");

        var icon = new Icon(stream);
        Cache[state] = icon;
        return icon;
    }

    private static string? ResolveEmbeddedResourceName(string fileName)
    {
        var suffix = fileName.Replace('\\', '.');
        return Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
