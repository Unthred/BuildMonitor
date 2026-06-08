using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;
using System.Windows.Media;
using BuildMonitor.Core.Settings;
using Microsoft.Win32;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;

namespace BuildMonitor.TrayApp.Services;

public enum ResolvedTheme
{
    Light,
    Dark
}

public static class ThemeService
{
    private const string ThemeDictionaryKey = "AppThemeDictionary";
    private static DispatcherTimer? systemThemeTimer;
    private static AppThemePreference? watchedPreference;
    private static bool lastObservedSystemDark;

    public static ResolvedTheme CurrentResolved { get; private set; } = ResolvedTheme.Light;

    public static event Action<ResolvedTheme>? ThemeChanged;

    public static ResolvedTheme Resolve(AppThemePreference preference) =>
        preference switch
        {
            AppThemePreference.Dark => ResolvedTheme.Dark,
            AppThemePreference.Light => ResolvedTheme.Light,
            _ => IsSystemDark() ? ResolvedTheme.Dark : ResolvedTheme.Light
        };

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key is null)
            {
                return false;
            }

            // AppsUseLightTheme: 0 = dark, 1 = light (DWORD)
            if (TryReadLightThemeValue(key.GetValue("AppsUseLightTheme"), out var appsDark))
            {
                return appsDark;
            }

            // Fallback for older Windows builds
            if (TryReadLightThemeValue(key.GetValue("SystemUsesLightTheme"), out var systemDark))
            {
                return systemDark;
            }
        }
        catch
        {
            // ignored
        }

        return false;
    }

    private static bool TryReadLightThemeValue(object? value, out bool isDark)
    {
        isDark = false;
        if (value is null)
        {
            return false;
        }

        var light = value switch
        {
            int i => i,
            long l => (int)l,
            byte b => b,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => (int?)null
        };

        if (light is null)
        {
            return false;
        }

        isDark = light == 0;
        return true;
    }

    public static void ApplyTheme(AppThemePreference preference)
    {
        var resolved = Resolve(preference);
        CurrentResolved = resolved;
        ApplyToApplication(resolved);
        ConfigureSystemThemeWatcher(preference);
        ThemeChanged?.Invoke(resolved);
    }

    public static void ApplyToApplication(ResolvedTheme theme)
    {
        var app = WpfApplication.Current;
        if (app is null)
        {
            return;
        }

        RemoveThemeDictionary(app);

        var uri = theme == ResolvedTheme.Dark
            ? new Uri("Themes/AppTheme.Dark.xaml", UriKind.Relative)
            : new Uri("Themes/AppTheme.Light.xaml", UriKind.Relative);

        var dictionary = new ResourceDictionary { Source = uri };
        dictionary["ThemeDictionaryKey"] = ThemeDictionaryKey;
        app.Resources.MergedDictionaries.Insert(0, dictionary);

        app.Resources["ThemeIsDark"] = theme == ResolvedTheme.Dark;
    }

    public static void ApplyToWindow(Window window, ResolvedTheme theme)
    {
        var palette = GetPalette(theme);
        window.Background = new SolidColorBrush(palette.Background);
        window.Foreground = new SolidColorBrush(palette.Foreground);
        AppIconService.ApplyToWindow(window);
        ApplyChrome(window, theme == ResolvedTheme.Dark);
    }

    public static void ApplyChrome(Window window, bool useDarkChrome)
    {
        window.SourceInitialized += (_, _) => SetDarkTitleBar(window, useDarkChrome);
        if (window.IsLoaded)
        {
            SetDarkTitleBar(window, useDarkChrome);
        }
    }

    public static ThemePalette GetPalette(ResolvedTheme theme) =>
        theme == ResolvedTheme.Dark
            ? new ThemePalette(
                Background: WpfColor.FromRgb(32, 32, 32),
                Foreground: WpfColor.FromRgb(230, 230, 230),
                CardBackground: WpfColor.FromRgb(45, 45, 48),
                Border: WpfColor.FromRgb(70, 70, 74),
                Accent: WpfColor.FromRgb(100, 180, 255))
            : new ThemePalette(
                Background: WpfColor.FromRgb(245, 245, 245),
                Foreground: WpfColor.FromRgb(30, 30, 30),
                CardBackground: WpfColors.White,
                Border: WpfColor.FromRgb(204, 204, 204),
                Accent: WpfColor.FromRgb(0, 102, 204));

    private static void ConfigureSystemThemeWatcher(AppThemePreference preference)
    {
        if (preference != AppThemePreference.System)
        {
            systemThemeTimer?.Stop();
            systemThemeTimer = null;
            watchedPreference = null;
            return;
        }

        if (watchedPreference == AppThemePreference.System && systemThemeTimer is not null)
        {
            return;
        }

        watchedPreference = AppThemePreference.System;
        lastObservedSystemDark = IsSystemDark();
        systemThemeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        systemThemeTimer.Tick -= OnSystemThemeTimerTick;
        systemThemeTimer.Tick += OnSystemThemeTimerTick;
        systemThemeTimer.Stop();
        systemThemeTimer.Start();
    }

    private static void OnSystemThemeTimerTick(object? sender, EventArgs e)
    {
        var dark = IsSystemDark();
        if (dark == lastObservedSystemDark)
        {
            return;
        }

        lastObservedSystemDark = dark;
        ApplyTheme(AppThemePreference.System);
    }

    private static void RemoveThemeDictionary(WpfApplication app)
    {
        for (var i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            var dict = app.Resources.MergedDictionaries[i];
            if (dict.Contains("ThemeDictionaryKey"))
            {
                app.Resources.MergedDictionaries.RemoveAt(i);
            }
        }
    }

    private static void SetDarkTitleBar(Window window, bool useDark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var attribute = 20; // DWMWA_USE_IMMERSIVE_DARK_MODE
        var value = useDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}

public sealed record ThemePalette(WpfColor Background, WpfColor Foreground, WpfColor CardBackground, WpfColor Border, WpfColor Accent);
