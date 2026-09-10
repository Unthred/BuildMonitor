using BuildMonitor.Core.Models;
using BuildMonitor.TrayApp.Services;

namespace BuildMonitor.Tests;

public sealed class TrayIconFactoryTests
{
    public static IEnumerable<object[]> StaticStates() =>
        Enum.GetValues<TrayHealthRing>()
            .Select(h => new object[] { h, TrayIconFactory.GetStaticResourceFileName(h) });

    [Theory]
    [MemberData(nameof(StaticStates))]
    public void Static_resource_names_match_convention(TrayHealthRing health, string expectedFileName)
    {
        Assert.Equal(expectedFileName, TrayIconFactory.GetStaticResourceFileName(health));
    }

    [Theory]
    [MemberData(nameof(StaticStates))]
    public void Every_static_ico_exists_and_loads(TrayHealthRing health, string expectedFileName)
    {
        TrayIconFactory.ClearCacheForTests();
        var assembly = typeof(TrayIconFactory).Assembly;
        Assert.Contains(
            assembly.GetManifestResourceNames(),
            n => n.EndsWith(expectedFileName, StringComparison.OrdinalIgnoreCase));
        var icon = TrayIconFactory.GetStaticIcon(health);
        Assert.NotNull(icon);
        Assert.True(icon.Handle != IntPtr.Zero);
    }

    [Theory]
    [InlineData(TrayHealthRing.Healthy)]
    [InlineData(TrayHealthRing.Attention)]
    [InlineData(TrayHealthRing.Neutral)]
    public void Active_frames_exist_and_load(TrayHealthRing health)
    {
        TrayIconFactory.ClearCacheForTests();
        for (var i = 0; i < TrayIconFactory.ActiveFrameCount; i++)
        {
            var name = TrayIconFactory.GetActiveResourceFileName(health, i);
            var assembly = typeof(TrayIconFactory).Assembly;
            Assert.Contains(
                assembly.GetManifestResourceNames(),
                n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
            var icon = TrayIconFactory.GetActiveFrameIcon(health, i);
            Assert.NotNull(icon);
        }
    }

    [Fact]
    public void Failed_active_frame_request_returns_static_failed()
    {
        TrayIconFactory.ClearCacheForTests();
        var staticFailed = TrayIconFactory.GetStaticIcon(TrayHealthRing.Failed);
        var viaActive = TrayIconFactory.GetActiveFrameIcon(TrayHealthRing.Failed, 3);
        Assert.Same(staticFailed, viaActive);
    }

    [Fact]
    public void Cache_returns_same_Icon_for_same_state()
    {
        TrayIconFactory.ClearCacheForTests();
        var first = TrayIconFactory.GetStaticIcon(TrayHealthRing.Healthy);
        var second = TrayIconFactory.GetStaticIcon(TrayHealthRing.Healthy);
        Assert.Same(first, second);
    }

    [Fact]
    public void Cache_returns_same_Icon_for_same_active_frame()
    {
        TrayIconFactory.ClearCacheForTests();
        var first = TrayIconFactory.GetActiveFrameIcon(TrayHealthRing.Healthy, 2);
        var second = TrayIconFactory.GetActiveFrameIcon(TrayHealthRing.Healthy, 2);
        Assert.Same(first, second);
    }

    [Fact]
    public void ClearCache_disposes_owned_icons()
    {
        TrayIconFactory.ClearCacheForTests();
        var icon = TrayIconFactory.GetStaticIcon(TrayHealthRing.Attention);
        var handle = icon.Handle;
        Assert.True(handle != IntPtr.Zero);
        TrayIconFactory.ClearCacheForTests();
        Assert.Equal(0, TrayIconFactory.CachedIconCountForTests);
    }

    [Fact]
    public void Animatable_presentation_uses_frame_icon()
    {
        TrayIconFactory.ClearCacheForTests();
        var presentation = new TrayIconPresentation(TrayHealthRing.Healthy, IsActive: true);
        var frame0 = TrayIconFactory.GetIcon(presentation, 0);
        var frame1 = TrayIconFactory.GetIcon(presentation, 1);
        Assert.NotSame(frame0, frame1);
    }

    [Fact]
    public void Failed_active_presentation_uses_static_icon()
    {
        TrayIconFactory.ClearCacheForTests();
        var presentation = new TrayIconPresentation(TrayHealthRing.Failed, IsActive: true);
        var icon = TrayIconFactory.GetIcon(presentation, 5);
        Assert.Same(TrayIconFactory.GetStaticIcon(TrayHealthRing.Failed), icon);
    }

    [Fact]
    public void GenerateTrayIcons_is_not_referenced_by_TrayApp()
    {
        var assembly = typeof(TrayIconFactory).Assembly;
        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "GenerateTrayIcons", StringComparison.OrdinalIgnoreCase));
        var csproj = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "src", "TrayApp", "BuildMonitor.TrayApp.csproj"));
        Assert.DoesNotContain("GenerateTrayIcons", csproj, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "BuildMonitor.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Repo root not found.");
    }
}
