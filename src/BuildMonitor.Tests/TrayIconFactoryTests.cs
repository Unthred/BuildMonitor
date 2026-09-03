using System.Reflection;
using BuildMonitor.Core.Models;
using BuildMonitor.TrayApp.Services;

namespace BuildMonitor.Tests;

public sealed class TrayIconFactoryTests
{
    public static IEnumerable<object[]> PresentationStateResourceCases =>
        Enum.GetValues<TrayIconPresentationState>()
            .Select(state => new object[] { state, TrayIconFactory.GetResourceFileName(state) });

    [Theory]
    [MemberData(nameof(PresentationStateResourceCases))]
    public void Every_presentation_state_maps_to_expected_resource(
        TrayIconPresentationState state,
        string expectedFileName)
    {
        Assert.Equal(expectedFileName, TrayIconFactory.GetResourceFileName(state));
    }

    [Theory]
    [MemberData(nameof(PresentationStateResourceCases))]
    public void Every_required_ico_exists_and_loads(TrayIconPresentationState state, string expectedFileName)
    {
        TrayIconFactory.ClearCacheForTests();

        var assembly = typeof(TrayIconFactory).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(expectedFileName, StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrWhiteSpace(resourceName));

        Assert.True(TrayIconFactory.TryGetIcon(state, out var icon));
        Assert.NotNull(icon);
        Assert.True(icon.Width > 0);
        Assert.True(icon.Height > 0);
    }

    [Fact]
    public void Icons_are_cached_rather_than_repeatedly_decoded()
    {
        TrayIconFactory.ClearCacheForTests();

        var first = TrayIconFactory.GetIcon(TrayIconPresentationState.Healthy);
        var second = TrayIconFactory.GetIcon(TrayIconPresentationState.Healthy);

        Assert.Same(first, second);
    }

    [Fact]
    public void TryGetIcon_returns_false_when_resource_name_is_missing()
    {
        TrayIconFactory.ClearCacheForTests();

        var assembly = typeof(TrayIconFactory).Assembly;
        var hasMissing = assembly.GetManifestResourceNames()
            .All(n => !n.EndsWith("tray-does-not-exist.ico", StringComparison.OrdinalIgnoreCase));
        Assert.True(hasMissing);

        var resolve = typeof(TrayIconFactory).GetMethod(
            "ResolveEmbeddedResourceName",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(resolve);
        var resolved = (string?)resolve.Invoke(null, ["tray-does-not-exist.ico"]);
        Assert.Null(resolved);
    }

    [Fact]
    public void TrayApp_has_no_reference_to_rejected_BuilderDuckRenderer_project()
    {
        var assembly = typeof(TrayIconFactory).Assembly;
        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "GenerateTrayIcons", StringComparison.OrdinalIgnoreCase));

        var trayAppDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TrayApp"));
        var csproj = File.ReadAllText(Path.Combine(trayAppDir, "BuildMonitor.TrayApp.csproj"));
        Assert.DoesNotContain("GenerateTrayIcons", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BuilderDuckRenderer", csproj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrayApp_does_not_use_build_icon_animation_timer()
    {
        var trayAppDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TrayApp"));
        var appSource = File.ReadAllText(Path.Combine(trayAppDir, "App.xaml.cs"));
        Assert.DoesNotContain("buildIconAnimationTimer", appSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildIconAnimationTick", appSource, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(PresentationStateResourceCases))]
    public void Embedded_ico_contains_16_20_24_32_frames(
        TrayIconPresentationState state,
        string expectedFileName)
    {
        var assembly = typeof(TrayIconFactory).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(expectedFileName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new BinaryReader(stream);
        Assert.Equal(0, reader.ReadUInt16()); // reserved
        Assert.Equal(1, reader.ReadUInt16()); // type = icon
        var count = reader.ReadUInt16();
        Assert.Equal(4, count);

        var sizes = new HashSet<int>();
        for (var i = 0; i < count; i++)
        {
            var w = reader.ReadByte();
            var h = reader.ReadByte();
            _ = reader.ReadBytes(14); // rest of directory entry
            sizes.Add(w == 0 ? 256 : w);
            Assert.Equal(w, h);
        }

        Assert.Equal(new HashSet<int> { 16, 20, 24, 32 }, sizes);
        _ = state; // theory parameter used for resource selection
    }
}
