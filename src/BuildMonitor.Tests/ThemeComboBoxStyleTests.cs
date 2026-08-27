using System.Text.RegularExpressions;

namespace BuildMonitor.Tests;

/// <summary>
/// Guards the #89 regression where keyed Settings ComboBox styles omitted BasedOn
/// and dropped dark-theme chrome (light system background + light foreground).
/// </summary>
public sealed class ThemeComboBoxStyleTests
{
    [Fact]
    public void Dark_and_light_ComboBox_theme_styles_set_both_background_and_foreground()
    {
        var dark = File.ReadAllText(FindRepoPath("src", "TrayApp", "Themes", "AppTheme.Dark.xaml"));
        var light = File.ReadAllText(FindRepoPath("src", "TrayApp", "Themes", "AppTheme.Light.xaml"));

        AssertComboBoxThemeBrushes(dark);
        AssertComboBoxThemeBrushes(light);
    }

    [Fact]
    public void SettingsFieldComboBox_style_is_based_on_theme_ComboBox()
    {
        var xaml = File.ReadAllText(FindRepoPath("src", "TrayApp", "SettingsWindow.xaml"));
        Assert.Matches(
            new Regex(
                """x:Key="SettingsFieldComboBox"[^>]*BasedOn="\{StaticResource \{x:Type ComboBox\}\}""",
                RegexOptions.Singleline),
            xaml);
        Assert.Matches(
            new Regex(
                """x:Key="SettingsFieldTextBox"[^>]*BasedOn="\{StaticResource \{x:Type TextBox\}\}""",
                RegexOptions.Singleline),
            xaml);
    }

    private static void AssertComboBoxThemeBrushes(string themeXaml)
    {
        var match = Regex.Match(
            themeXaml,
            """<Style\s+TargetType="\{x:Type ComboBox\}"[^>]*>.*?</Style>""",
            RegexOptions.Singleline);
        Assert.True(match.Success, "ComboBox TargetType style missing from theme.");
        var body = match.Value;
        Assert.Contains("Background", body, StringComparison.Ordinal);
        Assert.Contains("Foreground", body, StringComparison.Ordinal);
        Assert.Contains("ThemeControlBrush", body, StringComparison.Ordinal);
        Assert.Contains("ThemeForegroundBrush", body, StringComparison.Ordinal);
    }

    private static string FindRepoPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
