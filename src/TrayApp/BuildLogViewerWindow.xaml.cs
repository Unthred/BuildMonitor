using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using WpfClipboard = System.Windows.Clipboard;
using BuildMonitor.Core.Models;
using BuildMonitor.Infrastructure.LocalBuild;
using BuildMonitor.TrayApp.Services;

namespace BuildMonitor.TrayApp;

public partial class BuildLogViewerWindow : Window
{
    private enum IssueFilter
    {
        All,
        Errors,
        Warnings
    }

    private readonly BuildLogStore logStore;
    private readonly BuildLogViewerWindowStateStore windowStateStore;
    private readonly Func<string, BuildLogKind, LiveBuildLogView?>? getLiveBuildLog;
    private readonly DispatcherTimer liveRefreshTimer;
    private readonly string projectId;
    private readonly int maxDisplayBytes;
    private BuildLogRecord? currentRecord;
    private string currentLogText = string.Empty;
    private IReadOnlyList<LogIssue> allIssues = [];
    private IReadOnlyList<LogIssue> visibleIssues = [];
    private IReadOnlyList<Run> logLineRuns = [];
    private Run? highlightedLogRun;
    private bool suppressIssueSelectionSync;
    private BuildLogViewerWindowState windowState = new();
    private bool splitterRatioApplied;
    private bool wasLive;
    private bool wasWatchLive;
    private int lastRenderedRevision = -1;
    private bool isLoadingLog;

    public BuildLogViewerWindow(
        BuildLogStore logStore,
        BuildLogViewerWindowStateStore windowStateStore,
        string projectId,
        string projectName,
        int maxDisplayBytes,
        Func<string, BuildLogKind, LiveBuildLogView?>? getLiveBuildLog = null)
    {
        InitializeComponent();

        FilterAllRadio.Checked += IssueFilterChanged;
        FilterErrorsRadio.Checked += IssueFilterChanged;
        FilterWarningsRadio.Checked += IssueFilterChanged;

        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
        this.logStore = logStore;
        this.windowStateStore = windowStateStore;
        this.getLiveBuildLog = getLiveBuildLog;
        this.projectId = projectId;
        this.maxDisplayBytes = maxDisplayBytes;
        Title = $"Build Log — {projectName}";
        HeaderText.Text = projectName;

        liveRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        liveRefreshTimer.Tick += LiveRefreshTimerTick;

        LogKindCombo.Items.Add(BuildLogKind.Build);
        LogKindCombo.Items.Add(BuildLogKind.Run);
        LogKindCombo.Items.Add(BuildLogKind.Test);
        LogKindCombo.SelectedIndex = 0;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        windowState = await windowStateStore.LoadOrDefaultAsync();
        ApplyWindowState(windowState);
        ThemeService.ApplyToWindow(this, ThemeService.CurrentResolved);
        await LoadSelectedLogAsync();
        liveRefreshTimer.Start();
    }

    private void ApplyWindowState(BuildLogViewerWindowState state)
    {
        if (!double.IsNaN(state.Left) && !double.IsNaN(state.Top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = state.Left;
            Top = state.Top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (state.Width >= MinWidth)
        {
            Width = state.Width;
        }

        if (state.Height >= MinHeight)
        {
            Height = state.Height;
        }

        FollowOutputCheckBox.IsChecked = state.FollowOutput;

        Dispatcher.BeginInvoke(ApplySplitterRatio, DispatcherPriority.Loaded);
    }

    private void ApplySplitterRatio()
    {
        if (splitterRatioApplied)
        {
            return;
        }

        var ratio = Math.Clamp(windowState.LogPanelRatio, 0.2, 0.85);
        LogRow.Height = new GridLength(ratio, GridUnitType.Star);
        IssuesRow.Height = new GridLength(1.0 - ratio, GridUnitType.Star);
        splitterRatioApplied = true;
    }

    private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        liveRefreshTimer.Stop();
        CaptureWindowState();
        await windowStateStore.SaveAsync(windowState);
    }

    private void CaptureWindowState()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        windowState.Left = bounds.Left;
        windowState.Top = bounds.Top;
        windowState.Width = bounds.Width;
        windowState.Height = bounds.Height;

        var totalHeight = LogRow.ActualHeight + IssuesRow.ActualHeight;
        if (totalHeight > 0)
        {
            windowState.LogPanelRatio = LogRow.ActualHeight / totalHeight;
        }

        windowState.FollowOutput = FollowOutputCheckBox.IsChecked == true;
    }

    private async void LogKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        wasLive = false;
        wasWatchLive = false;
        lastRenderedRevision = -1;
        await LoadSelectedLogAsync();
    }

    private void IssueFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyIssueFilter();
    }

    private void LiveRefreshTimerTick(object? sender, EventArgs e)
    {
        if (!IsLoaded || isLoadingLog || getLiveBuildLog is null)
        {
            return;
        }

        if (LogKindCombo.SelectedItem is not BuildLogKind kind)
        {
            return;
        }

        var live = getLiveBuildLog(projectId, kind);
        if (live?.IsLive == true)
        {
            if (live.Revision == lastRenderedRevision)
            {
                return;
            }

            var followTail = ShouldFollowOutput();
            wasLive = true;
            wasWatchLive = live.State is ProjectLifecycleState.Watching or ProjectLifecycleState.BuildFailed;
            lastRenderedRevision = live.Revision;
            ApplyLiveDisplay(live, followTail);
            return;
        }

        if (wasLive)
        {
            wasLive = false;
            lastRenderedRevision = -1;

            if (wasWatchLive)
            {
                wasWatchLive = false;
                BuildTimeText.Text = "Watch rebuild finished";
                FooterText.Text = "Live watch output (saved build log is from the last full build)";
                return;
            }

            _ = LoadSelectedLogAsync();
        }
    }

    private void ApplyLiveDisplay(LiveBuildLogView live, bool followTail)
    {
        currentRecord = null;
        currentLogText = BuildLogTextNormalizer.Normalize(
            BuildLogStore.TruncateTailForDisplay(live.Text, maxDisplayBytes));
        allIssues = BuildLogParser.ParseIssues(currentLogText);
        RenderLogText();
        ApplyIssueFilter();

        BuildTimeText.Text = live.State switch
        {
            ProjectLifecycleState.Building => "Build in progress…",
            ProjectLifecycleState.Watching => "Watch rebuild in progress…",
            _ => "Build in progress…"
        };
        FooterText.Text =
            $"{live.ErrorCount} errors | {live.WarningCount} warnings | live output";

        if (followTail)
        {
            ScrollLogToEnd();
        }
    }

    private async Task LoadSelectedLogAsync()
    {
        if (LogKindCombo.SelectedItem is not BuildLogKind kind)
        {
            return;
        }

        isLoadingLog = true;
        try
        {
            await LoadSelectedLogCoreAsync(kind);
        }
        finally
        {
            isLoadingLog = false;
        }
    }

    private async Task LoadSelectedLogCoreAsync(BuildLogKind kind)
    {
        currentRecord = await logStore.LoadMetadataAsync(projectId, kind);
        if (currentRecord is null)
        {
            currentLogText = "No log available for this type yet.";
            allIssues = [];
            visibleIssues = [];
            RenderLogText();
            ErrorsList.ItemsSource = null;
            IssueSummaryText.Text = string.Empty;
            BuildTimeText.Text = string.Empty;
            FooterText.Text = string.Empty;
            UpdateNavigationButtons();
            return;
        }

        currentLogText = BuildLogTextNormalizer.Normalize(
            await logStore.LoadLogTextAsync(currentRecord, maxDisplayBytes));
        allIssues = BuildLogParser.ParseIssues(currentLogText);
        RenderLogText();
        ApplyIssueFilter(selectFirstIssue: allIssues.Count > 0);

        var errorCount = BuildLogParser.ParseErrorCount(currentLogText);
        var warningCount = BuildLogParser.ParseWarningCount(currentLogText);
        var finishedLocal = BuildTimestampFormatter.FormatLocal(currentRecord.FinishedAtUtc);
        var kindLabel = kind switch
        {
            BuildLogKind.Test => "Last test",
            BuildLogKind.Run => "Last run output",
            _ => "Last build"
        };
        BuildTimeText.Text = $"{kindLabel}: {finishedLocal}";
        FooterText.Text =
            $"{currentRecord.CommandLine} | exit {currentRecord.ExitCode} | {errorCount} errors | {warningCount} warnings | duration {currentRecord.FinishedAtUtc - currentRecord.StartedAtUtc:g}";

        if (ShouldFollowOutput())
        {
            ScrollLogToEnd();
        }
    }

    private void FollowOutputChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (ShouldFollowOutput())
        {
            ScrollLogToEnd();
        }
    }

    private bool ShouldFollowOutput() => FollowOutputCheckBox.IsChecked == true;

    private void ScrollLogToEnd()
    {
        var scrollViewer = FindScrollViewer(LogText);
        scrollViewer?.ScrollToEnd();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer viewer)
        {
            return viewer;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(element, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void ApplyIssueFilter(bool selectFirstIssue = false)
    {
        var filter = GetCurrentFilter();
        visibleIssues = filter switch
        {
            IssueFilter.Errors => allIssues.Where(i => i.IsError).ToList(),
            IssueFilter.Warnings => allIssues.Where(i => !i.IsError).ToList(),
            _ => allIssues
        };

        ErrorsList.ItemsSource = visibleIssues;

        var errorCount = allIssues.Count(i => i.IsError);
        var warningCount = allIssues.Count(i => !i.IsError);
        IssueSummaryText.Text = filter switch
        {
            IssueFilter.Errors => $"{visibleIssues.Count} of {errorCount} errors shown",
            IssueFilter.Warnings => $"{visibleIssues.Count} of {warningCount} warnings shown",
            _ => $"{errorCount} errors, {warningCount} warnings",
        };

        if (filter == IssueFilter.Errors && errorCount == 0)
        {
            FilterAllRadio.IsChecked = true;
            return;
        }

        if (selectFirstIssue && visibleIssues.Count > 0)
        {
            SelectIssueAt(0);
        }
        else
        {
            BuildLogHighlighter.ClearHighlight(ref highlightedLogRun);
            UpdateNavigationButtons();
        }
    }

    private IssueFilter GetCurrentFilter()
    {
        if (FilterErrorsRadio?.IsChecked == true)
        {
            return IssueFilter.Errors;
        }

        if (FilterWarningsRadio?.IsChecked == true)
        {
            return IssueFilter.Warnings;
        }

        return IssueFilter.All;
    }

    private void RenderLogText()
    {
        BuildLogHighlighter.ClearHighlight(ref highlightedLogRun);
        var palette = ThemeService.GetPalette(ThemeService.CurrentResolved);
        logLineRuns = BuildLogHighlighter.Apply(
            LogText,
            currentLogText,
            palette,
            ThemeService.CurrentResolved);
    }

    private void ErrorsListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressIssueSelectionSync)
        {
            return;
        }

        if (ErrorsList.SelectedItem is LogIssue issue)
        {
            HighlightIssueInLog(issue);
        }
        else
        {
            BuildLogHighlighter.ClearHighlight(ref highlightedLogRun);
        }

        UpdateNavigationButtons();
    }

    private void HighlightIssueInLog(LogIssue issue)
    {
        try
        {
            BuildLogHighlighter.HighlightIssue(
                LogText,
                logLineRuns,
                ref highlightedLogRun,
                issue,
                ThemeService.CurrentResolved);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Log navigation failed: {ex}");
        }
    }

    private void SelectIssueAt(int index)
    {
        if (index < 0 || index >= visibleIssues.Count)
        {
            ErrorsList.SelectedItem = null;
            BuildLogHighlighter.ClearHighlight(ref highlightedLogRun);
            UpdateNavigationButtons();
            return;
        }

        suppressIssueSelectionSync = true;
        try
        {
            ErrorsList.SelectedIndex = index;
            ErrorsList.ScrollIntoView(visibleIssues[index]);
        }
        finally
        {
            suppressIssueSelectionSync = false;
        }

        HighlightIssueInLog(visibleIssues[index]);
        UpdateNavigationButtons();
    }

    private void PreviousIssueClicked(object sender, RoutedEventArgs e) =>
        NavigateIssue(-1);

    private void NextIssueClicked(object sender, RoutedEventArgs e) =>
        NavigateIssue(1);

    private void NavigateIssue(int delta)
    {
        if (visibleIssues.Count == 0)
        {
            return;
        }

        var index = ErrorsList.SelectedIndex;
        if (index < 0)
        {
            index = delta > 0 ? 0 : visibleIssues.Count - 1;
        }
        else
        {
            index = Math.Clamp(index + delta, 0, visibleIssues.Count - 1);
        }

        SelectIssueAt(index);
    }

    private void UpdateNavigationButtons()
    {
        var hasIssues = visibleIssues.Count > 0;
        var index = ErrorsList.SelectedIndex;

        PreviousIssueButton.IsEnabled = hasIssues && index > 0;
        NextIssueButton.IsEnabled = hasIssues && (index < 0 || index < visibleIssues.Count - 1);
    }

    private void CopyAllClicked(object sender, RoutedEventArgs e) =>
        WpfClipboard.SetText(currentLogText);

    private void CopySelectionClicked(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(LogText.Selection.Text))
        {
            WpfClipboard.SetText(LogText.Selection.Text);
        }
    }

    private void CopyErrorsClicked(object sender, RoutedEventArgs e)
    {
        if (visibleIssues.Count == 0)
        {
            return;
        }

        WpfClipboard.SetText(string.Join(Environment.NewLine, visibleIssues.Select(i => i.Text)));
    }

    private void OpenLogFileClicked(object sender, RoutedEventArgs e)
    {
        if (currentRecord is null || !File.Exists(currentRecord.LogFilePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(currentRecord.LogFilePath) { UseShellExecute = true });
    }
}
