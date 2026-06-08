using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;
using WpfClipboard = System.Windows.Clipboard;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;

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
        var toast = CreateToastWindow(title, message, kind);
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

    private static Window CreateToastWindow(string title, string message, ToastKind kind)
    {
        var palette = ThemeService.GetPalette(ThemeService.CurrentResolved);
        var accent = AccentBrush(kind, palette);
        var copyText = $"{title}{Environment.NewLine}{Environment.NewLine}{message}";

        var border = new Border
        {
            Background = new SolidColorBrush(palette.CardBackground),
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            MaxWidth = 420,
            Cursor = WpfCursors.Hand,
            ToolTip = "Click to copy to clipboard",
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 12,
                ShadowDepth = 2,
                Opacity = 0.35
            }
        };

        var titleBlock = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            TextWrapping = TextWrapping.Wrap
        };
        var messageBlock = new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(palette.Foreground),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Opacity = 0.95
        };

        var panel = new StackPanel();
        panel.Children.Add(titleBlock);
        panel.Children.Add(messageBlock);
        border.Child = panel;

        border.MouseLeftButtonUp += (_, _) =>
        {
            try
            {
                WpfClipboard.SetText(copyText);
                titleBlock.Text = "Copied to clipboard";
                messageBlock.Text = title;
                messageBlock.Opacity = 0.85;
                border.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(40, 167, 69));
            }
            catch (Exception ex)
            {
                titleBlock.Text = "Copy failed";
                messageBlock.Text = ex.Message;
                border.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(220, 53, 69));
            }
        };

        return new Window
        {
            Content = border,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = null,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = false,
            Focusable = false,
            SizeToContent = SizeToContent.WidthAndHeight
        };
    }

    private static SolidColorBrush AccentBrush(ToastKind kind, ThemePalette palette) =>
        kind switch
        {
            ToastKind.Success => new SolidColorBrush(WpfColor.FromRgb(40, 167, 69)),
            ToastKind.Warning => new SolidColorBrush(WpfColor.FromRgb(255, 193, 7)),
            ToastKind.Error => new SolidColorBrush(WpfColor.FromRgb(220, 53, 69)),
            _ => new SolidColorBrush(palette.Accent)
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
            var height = toast.ActualHeight > 0 ? toast.ActualHeight : 80;
            var width = toast.ActualWidth > 0 ? toast.ActualWidth : 280;

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
