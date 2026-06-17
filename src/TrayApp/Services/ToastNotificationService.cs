using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;
using WpfClipboard = System.Windows.Clipboard;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace BuildMonitor.TrayApp.Services;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error
}

public static class ToastNotificationService
{
    private const int MaxVisibleToasts = 4;
    private const double ToastSpacing = 8;
    private const double ScreenMargin = 16;
    private const double ToastWidth = 285;
    private const double ToastHeight = 60;
    private const double ToastCornerRadius = 16;
    private const int MaxMessageChars = 90;
    private const int MaxMessageLines = 2;

    private static readonly List<ToastEntry> VisibleToasts = [];

    private static ToastPosition position = ToastPosition.BottomRight;
    private static TimeSpan displayDuration = TimeSpan.FromSeconds(7);
    private static ToastNotificationSettings notifications = new();

    public static void ApplySettings(AppBehaviorSettings settings)
    {
        position = settings.ToastPosition;
        displayDuration = TimeSpan.FromSeconds(Math.Clamp(settings.ToastDurationSeconds, 2, 120));
        notifications = settings.Toasts ?? new ToastNotificationSettings();
    }

    public static bool ShouldShow(UserNotificationCategory category) =>
        category switch
        {
            UserNotificationCategory.BuildStart => notifications.BuildStart,
            UserNotificationCategory.BuildSuccess => notifications.BuildSuccess,
            UserNotificationCategory.BuildFailure => notifications.BuildFailure,
            UserNotificationCategory.FileChangeDetected => notifications.FileChangeDetected,
            UserNotificationCategory.Warning => notifications.Warnings,
            UserNotificationCategory.Error => notifications.Errors,
            UserNotificationCategory.Info => notifications.Info,
            _ => false
        };

    public static void ShowIfEnabled(
        string title,
        string message,
        ToastKind kind,
        UserNotificationCategory category)
    {
        if (!ShouldShow(category))
        {
            return;
        }

        Show(title, message, kind);
    }

    public static void ShowInfo(string title, string message) =>
        Show(title, message, ToastKind.Info);

    public static void ShowSuccess(string title, string message) =>
        Show(title, message, ToastKind.Success);

    public static void ShowWarning(string title, string message) =>
        Show(title, message, ToastKind.Warning);

    public static void ShowError(string title, string message) =>
        Show(title, message, ToastKind.Error);

    public static string FormatException(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            return string.Join(
                Environment.NewLine,
                aggregate.Flatten().InnerExceptions.Select(FormatException));
        }

        var details = exception.GetType().Name;
        if (!string.IsNullOrWhiteSpace(exception.Message))
        {
            details = $"{exception.Message} ({exception.GetType().Name})";
        }

        if (exception.InnerException is not null)
        {
            details += Environment.NewLine + FormatException(exception.InnerException);
        }

        return details;
    }

    public static void Show(string title, string message, ToastKind kind)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return;
        }

        app.Dispatcher.BeginInvoke(() => ShowOnUiThread(title, message, kind));
    }

    public static void CloseAll()
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return;
        }

        app.Dispatcher.Invoke(() =>
        {
            foreach (var entry in VisibleToasts.ToList())
            {
                entry.AutoCloseTimer?.Stop();
                try
                {
                    entry.Window.Close();
                }
                catch
                {
                    // ignore during shutdown
                }
            }

            VisibleToasts.Clear();
        });
    }

    private static void ShowOnUiThread(string title, string message, ToastKind kind)
    {
        var (displayMessage, isTruncated) = GetDisplayMessage(message);
        var toast = CreateToastWindow(title, displayMessage, message, isTruncated, kind);
        var entry = new ToastEntry(toast, title, message);
        toast.ContentRendered += (_, _) => RepositionToasts();

        VisibleToasts.Insert(0, entry);
        while (VisibleToasts.Count > MaxVisibleToasts)
        {
            VisibleToasts[^1].Window.Close();
        }

        RepositionToasts();
        toast.Show();

        var timer = new DispatcherTimer { Interval = displayDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            toast.Close();
        };
        timer.Start();
        entry.AutoCloseTimer = timer;

        toast.Closed += (_, _) =>
        {
            entry.AutoCloseTimer?.Stop();
            VisibleToasts.Remove(entry);
            RepositionToasts();
        };
    }

    private static (string display, bool truncated) GetDisplayMessage(string message)
    {
        var normalized = message.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        if (lines.Length > MaxMessageLines)
        {
            return (string.Join(Environment.NewLine, lines.Take(MaxMessageLines)) + "...", true);
        }

        if (normalized.Length > MaxMessageChars)
        {
            return (normalized[..(MaxMessageChars - 3)] + "...", true);
        }

        return (message, false);
    }

    private static Window CreateToastWindow(
        string title,
        string displayMessage,
        string fullMessage,
        bool isTruncated,
        ToastKind kind)
    {
        var resolvedTheme = ThemeService.CurrentResolved;
        var palette = ThemeService.GetPalette(resolvedTheme);
        var accentColor = AccentColor(kind, palette);
        var accent = new SolidColorBrush(accentColor);
        var defaultBorder = new SolidColorBrush(palette.Border);
        var copyText = $"{title}{Environment.NewLine}{Environment.NewLine}{fullMessage}";

        var iconBlock = new TextBlock
        {
            Text = IconGlyph(kind),
            FontFamily = new WpfFontFamily("Segoe UI Emoji"),
            FontSize = 17,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center
        };

        var iconHost = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = new SolidColorBrush(Blend(palette.CardBackground, accentColor, 0.22)),
            Child = iconBlock,
            VerticalAlignment = WpfVerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var accentStrip = new Border
        {
            Background = accent,
            HorizontalAlignment = WpfHorizontalAlignment.Stretch
        };

        var card = new Border
        {
            Background = new SolidColorBrush(Blend(palette.CardBackground, accentColor, 0.07)),
            BorderBrush = defaultBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(ToastCornerRadius),
            ClipToBounds = true,
            Width = ToastWidth - 2,
            Height = ToastHeight - 2,
            Cursor = WpfCursors.Hand,
            Opacity = 0,
            ToolTip = isTruncated ? "Click to show full message" : "Click to copy to clipboard",
            Effect = CreateShadow(resolvedTheme)
        };

        var titleBlock = new TextBlock
        {
            Text = title,
            FontFamily = new WpfFontFamily("Segoe UI"),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(palette.Foreground),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        var messageBlock = new TextBlock
        {
            Text = displayMessage,
            FontFamily = new WpfFontFamily("Segoe UI"),
            FontSize = 11,
            LineHeight = 14,
            Foreground = new SolidColorBrush(palette.Foreground),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = ToastHeight - 30,
            Margin = new Thickness(0, 2, 0, 0),
            Opacity = 0.72
        };

        var textPanel = new StackPanel { VerticalAlignment = WpfVerticalAlignment.Center };
        textPanel.Children.Add(titleBlock);
        textPanel.Children.Add(messageBlock);

        var body = new Grid { Margin = new Thickness(9, 5, 10, 5) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(iconHost, 0);
        Grid.SetColumn(textPanel, 1);
        body.Children.Add(iconHost);
        body.Children.Add(textPanel);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(accentStrip, 0);
        Grid.SetColumn(body, 1);
        root.Children.Add(accentStrip);
        root.Children.Add(body);
        card.Child = root;

        var expanded = false;
        var window = new Window
        {
            Content = card,
            Width = ToastWidth,
            Height = ToastHeight,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = null,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = false,
            Focusable = false,
            SizeToContent = SizeToContent.Manual
        };

        card.Loaded += (_, _) =>
            card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });

        card.MouseEnter += (_, _) => card.BorderBrush = accent;
        card.MouseLeave += (_, _) => card.BorderBrush = defaultBorder;

        card.MouseLeftButtonUp += (_, _) =>
        {
            if (isTruncated && !expanded)
            {
                expanded = true;
                messageBlock.Text = fullMessage;
                messageBlock.TextTrimming = TextTrimming.None;
                messageBlock.MaxHeight = double.PositiveInfinity;
                card.ClearValue(FrameworkElement.HeightProperty);
                card.MinHeight = ToastHeight - 2;
                card.ToolTip = "Click to copy to clipboard";
                window.SizeToContent = SizeToContent.Height;
                window.ClearValue(FrameworkElement.HeightProperty);
                window.UpdateLayout();
                RepositionToasts();
                return;
            }

            try
            {
                WpfClipboard.SetText(copyText);
                iconBlock.Text = "📋";
                titleBlock.Text = "Copied to clipboard";
                messageBlock.Text = title;
                messageBlock.Opacity = 0.72;
                messageBlock.TextTrimming = TextTrimming.None;
                messageBlock.MaxHeight = double.PositiveInfinity;
                var successColor = WpfColor.FromRgb(40, 167, 69);
                accentStrip.Background = new SolidColorBrush(successColor);
                card.Background = new SolidColorBrush(Blend(palette.CardBackground, successColor, 0.1));
                iconHost.Background = new SolidColorBrush(Blend(palette.CardBackground, successColor, 0.22));
                card.BorderBrush = new SolidColorBrush(successColor);
                card.ToolTip = null;
            }
            catch (Exception ex)
            {
                titleBlock.Text = "Copy failed";
                messageBlock.Text = ex.Message;
                var failureColor = WpfColor.FromRgb(220, 53, 69);
                accentStrip.Background = new SolidColorBrush(failureColor);
                card.BorderBrush = new SolidColorBrush(failureColor);
                card.ToolTip = null;
            }
        };

        return window;
    }

    private static string IconGlyph(ToastKind kind) =>
        kind switch
        {
            ToastKind.Success => "🎉",
            ToastKind.Warning => "😬",
            ToastKind.Error => "🤬",
            _ => "📢"
        };

    private static WpfColor AccentColor(ToastKind kind, ThemePalette palette) =>
        kind switch
        {
            ToastKind.Success => WpfColor.FromRgb(40, 167, 69),
            ToastKind.Warning => WpfColor.FromRgb(255, 193, 7),
            ToastKind.Error => WpfColor.FromRgb(220, 53, 69),
            _ => palette.Accent
        };

    private static WpfColor Blend(WpfColor from, WpfColor to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return WpfColor.FromRgb(
            (byte)(from.R + (to.R - from.R) * amount),
            (byte)(from.G + (to.G - from.G) * amount),
            (byte)(from.B + (to.B - from.B) * amount));
    }

    private static System.Windows.Media.Effects.Effect CreateShadow(ResolvedTheme theme) =>
        new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = theme == ResolvedTheme.Dark ? 20 : 16,
            ShadowDepth = theme == ResolvedTheme.Dark ? 0 : 2,
            Opacity = theme == ResolvedTheme.Dark ? 0.5 : 0.2,
            Color = WpfColor.FromRgb(0, 0, 0)
        };

    private static void RepositionToasts()
    {
        var workArea = SystemParameters.WorkArea;
        var isTop = position is ToastPosition.TopRight or ToastPosition.TopLeft;
        var isLeft = position is ToastPosition.BottomLeft or ToastPosition.TopLeft;
        var y = isTop ? workArea.Top + ScreenMargin : workArea.Bottom - ScreenMargin;

        foreach (var entry in VisibleToasts)
        {
            var toast = entry.Window;
            toast.UpdateLayout();
            var height = toast.ActualHeight > 0 ? toast.ActualHeight : ToastHeight;
            var width = toast.ActualWidth > 0 ? toast.ActualWidth : ToastWidth;

            if (isTop)
            {
                toast.Top = y;
                y += height + ToastSpacing;
            }
            else
            {
                y -= height;
                toast.Top = y;
                y -= ToastSpacing;
            }

            toast.Left = isLeft
                ? workArea.Left + ScreenMargin
                : workArea.Right - width - ScreenMargin;
        }
    }

    private sealed class ToastEntry(Window window, string title, string message)
    {
        public Window Window { get; } = window;
        public string Title { get; } = title;
        public string Message { get; } = message;
        public DispatcherTimer? AutoCloseTimer { get; set; }
    }
}
