using System.Drawing;
using System.Reflection;
using BuildMonitor.Core.Models;

namespace BuildMonitor.TrayApp.Services;

/// <summary>
/// Loads committed tray health-ring icons (#129). Static + pre-rendered active frames — no runtime drawing.
/// Cache owns all <see cref="Icon"/> instances.
/// </summary>
public static class TrayIconFactory
{
    public const int ActiveFrameCount = 12;

    private static readonly Dictionary<string, Icon> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Icon GetStaticIcon(TrayHealthRing health) =>
        GetCached(GetStaticResourceFileName(health));

    public static Icon GetActiveFrameIcon(TrayHealthRing health, int frame)
    {
        if (health == TrayHealthRing.Failed)
        {
            return GetStaticIcon(TrayHealthRing.Failed);
        }

        var index = ((frame % ActiveFrameCount) + ActiveFrameCount) % ActiveFrameCount;
        return GetCached(GetActiveResourceFileName(health, index));
    }

    public static Icon GetIcon(TrayIconPresentation presentation, int animationFrame = 0)
    {
        if (!presentation.IsAnimatable)
        {
            return GetStaticIcon(presentation.Health);
        }

        return GetActiveFrameIcon(presentation.Health, animationFrame);
    }

    public static bool TryGetIcon(TrayIconPresentation presentation, int animationFrame, out Icon? icon)
    {
        try
        {
            icon = GetIcon(presentation, animationFrame);
            return true;
        }
        catch (InvalidOperationException)
        {
            icon = null;
            return false;
        }
    }

    internal static string GetStaticResourceFileName(TrayHealthRing health) =>
        health switch
        {
            TrayHealthRing.Healthy => "tray-healthy.ico",
            TrayHealthRing.Attention => "tray-attention.ico",
            TrayHealthRing.Failed => "tray-failed.ico",
            _ => "tray-neutral.ico"
        };

    internal static string GetActiveResourceFileName(TrayHealthRing health, int frame) =>
        health switch
        {
            TrayHealthRing.Healthy => $"tray-healthy-a{frame:00}.ico",
            TrayHealthRing.Attention => $"tray-attention-a{frame:00}.ico",
            TrayHealthRing.Neutral => $"tray-neutral-a{frame:00}.ico",
            _ => GetStaticResourceFileName(TrayHealthRing.Failed)
        };

    internal static int CachedIconCountForTests => Cache.Count;

    internal static void ClearCacheForTests()
    {
        // Snapshot first: a DispatcherTimer tick from another test can load icons while we clear.
        var icons = Cache.Values.ToArray();
        Cache.Clear();
        foreach (var icon in icons)
        {
            icon.Dispose();
        }
    }

    private static Icon GetCached(string fileName)
    {
        if (Cache.TryGetValue(fileName, out var cached))
        {
            return cached;
        }

        var resourceName = ResolveEmbeddedResourceName(fileName);
        if (resourceName is null)
        {
            throw new InvalidOperationException($"Tray icon resource not found: {fileName}");
        }

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Tray icon stream missing: {fileName}");
        }

        var icon = new Icon(stream);
        Cache[fileName] = icon;
        return icon;
    }

    private static string? ResolveEmbeddedResourceName(string fileName)
    {
        var suffix = fileName.Replace('\\', '.');
        return Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
