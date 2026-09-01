using System.Drawing;
using System.Reflection;
using BuildMonitor.Core.Models;

namespace BuildMonitor.TrayApp.Services;

/// <summary>
/// Loads committed builder-duck tray icons (#95). Static embedded assets only — no runtime drawing.
/// </summary>
public static class TrayIconFactory
{
    private static readonly Dictionary<TrayIconPresentationState, Icon> Cache = new();

    public static Icon GetIcon(TrayIconPresentationState state)
    {
        if (!TryGetIcon(state, out var icon) || icon is null)
        {
            throw new InvalidOperationException($"Tray icon unavailable for state {state}.");
        }

        return icon;
    }

    public static bool TryGetIcon(TrayIconPresentationState state, out Icon? icon)
    {
        if (Cache.TryGetValue(state, out icon))
        {
            return icon is not null;
        }

        var fileName = GetResourceFileName(state);
        var resourceName = ResolveEmbeddedResourceName(fileName);
        if (resourceName is null)
        {
            icon = null;
            return false;
        }

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            icon = null;
            return false;
        }

        try
        {
            icon = new Icon(stream);
            Cache[state] = icon;
            return true;
        }
        catch (ArgumentException)
        {
            icon = null;
            return false;
        }
    }

    internal static string GetResourceFileName(TrayIconPresentationState state) =>
        state switch
        {
            TrayIconPresentationState.Healthy => "tray-healthy.ico",
            TrayIconPresentationState.Building => "tray-building.ico",
            TrayIconPresentationState.Attention => "tray-attention.ico",
            TrayIconPresentationState.Failed => "tray-failed.ico",
            _ => "tray-neutral.ico"
        };

    internal static void ClearCacheForTests()
    {
        foreach (var icon in Cache.Values)
        {
            icon.Dispose();
        }

        Cache.Clear();
    }

    private static string? ResolveEmbeddedResourceName(string fileName)
    {
        var suffix = fileName.Replace('\\', '.');
        return Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
