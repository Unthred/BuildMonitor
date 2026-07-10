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
    private readonly AppWindowsLayoutStore windowsLayoutStore;
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
    private BuildLogViewerLayoutState windowState = new();
    private BuildLogKind currentLogKind = BuildLogKind.Build;
    private bool splitterRatioApplied;
    private bool issuesCarriedFromPreviousBuild;
    private int resolvedDisplayErrorCount;
    private int resolvedDisplayWarningCount;
    private bool wasLive;
    private bool wasWatchLive;
    private int lastRenderedRevision = -1;
    private bool isLoadingLog;

    private bool followVirtualDesktop = true;

    public BuildLogViewerWindow(
        BuildLogStore logStore,
        AppWindowsLayoutStore windowsLayoutStore,
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
        Activated += OnWindowActivated;
        Closing += OnWindowClosing;
        this.logStore = logStore;
        this.windowsLayoutStore = windowsLayoutStore;
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
        windowState = windowsLayoutStore.Layout.BuildLog;
        ApplyWindowState(windowState);
        if (double.IsNaN(windowState.Left) || double.IsNaN(windowState.Top))
        {
            TrayScreenPlacement.PlaceWindowCentered(this);
        }

        ThemeService.ApplyToWindow(this, ThemeService.CurrentResolved);
        await LoadSelectedLogAsync();
        liveRefreshTimer.Start();
        TryFollowVirtualDesktop();
    }

    public void ConfigureVirtualDesktopFollow(bool enabled) => followVirtualDesktop = enabled;

    public void TryFollowVirtualDesktop() =>
        WindowVirtualDesktopPlacement.TryFollow(this, followVirtualDesktop);

    private void OnWindowActivated(object? sender, EventArgs e) => TryFollowVirtualDesktop();

    private void ApplyWindowState(BuildLogViewerLayoutState state)
    {
        WindowLayoutService.Apply(this, state, 960, 720);

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
        await windowsLayoutStore.SaveAsync();
    }

    private void CaptureWindowState()
    {
        WindowLayoutService.Capture(this, windowState);

        var totalHeight = LogRow.ActualHeight + IssuesRow.ActualHeight;
        if (totalHeight > 0)
        {
            var ratio = LogRow.ActualHeight / totalHeight;
            if (double.IsFinite(ratio))
            {
                windowState.LogPanelRatio = Math.Clamp(ratio, 0.2, 0.85);
            }
        }

        windowState.FollowOutput = FollowOutputCheckBox.IsChecked == true;
    }

    public void SelectLogKind(BuildLogKind kind)
    {
        LogKindCombo.SelectedItem = kind;
    }

    public void SelectErrorsFilter()
    {
        void Apply()
        {
            FilterErrorsRadio.IsChecked = true;
            ApplyIssueFilter(selectFirstIssue: true);
        }

        if (!IsLoaded)
        {
            Loaded += OnLoadedSelectErrors;

            void OnLoadedSelectErrors(object? sender, RoutedEventArgs e)
            {
                Loaded -= OnLoadedSelectErrors;
                Apply();
            }

            return;
        }

        Apply();
    }

    public void SelectWarningsFilter()
    {
        void Apply()
        {
            FilterWarningsRadio.IsChecked = true;
            ApplyIssueFilter(selectFirstIssue: true);
        }

        if (!IsLoaded)
        {
            Loaded += OnLoadedSelectWarnings;

            void OnLoadedSelectWarnings(object? sender, RoutedEventArgs e)
            {
                Loaded -= OnLoadedSelectWarnings;
                Apply();
            }

            return;
        }

        Apply();
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
            ApplyLiveDisplay(live, kind, followTail);
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

    private void ApplyLiveDisplay(LiveBuildLogView live, BuildLogKind kind, bool followTail)
    {
        currentRecord = null;
        currentLogText = BuildLogTextNormalizer.Normalize(
            BuildLogStore.TruncateTailForDisplay(live.Text, maxDisplayBytes));
        currentLogKind = kind;
        allIssues = ParseIssuesForCurrentLog();
        RenderLogText();
        RefreshResolvedIssueCounts(live);
        ApplyIssueFilter();

        BuildTimeText.Text = live.State switch
        {
            ProjectLifecycleState.Building => "Build in progress…",
            ProjectLifecycleState.Watching => "Watch rebuild in progress…",
            ProjectLifecycleState.Testing => "Tests in progress…",
            _ => "Build in progress…"
        };
        FooterText.Text = FormatFooterText(
            resolvedDisplayErrorCount,
            resolvedDisplayWarningCount,
            isLive: true);

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

        currentLogKind = kind;
        currentLogText = BuildLogTextNormalizer.Normalize(
            await logStore.LoadLogTextAsync(currentRecord, maxDisplayBytes));
        allIssues = ParseIssuesForCurrentLog();
        RenderLogText();
        RefreshResolvedIssueCounts();
        ApplyIssueFilter(selectFirstIssue: allIssues.Count > 0);

        var finishedLocal = BuildTimestampFormatter.FormatLocal(currentRecord.FinishedAtUtc);
        var kindLabel = kind switch
        {
            BuildLogKind.Test => "Last test",
            BuildLogKind.Run => "Last run output",
            _ => "Last build"
        };
        BuildTimeText.Text = $"{kindLabel}: {finishedLocal}";
        FooterText.Text =
            $"{currentRecord.CommandLine} | exit {currentRecord.ExitCode} | {FormatFooterText(resolvedDisplayErrorCount, resolvedDisplayWarningCount, isLive: false)} | duration {currentRecord.FinishedAtUtc - currentRecord.StartedAtUtc:g}";

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

        var errorCount = resolvedDisplayErrorCount;
        var warningCount = resolvedDisplayWarningCount;
        UpdateErrorBanner(errorCount);

        var carryNote = issuesCarriedFromPreviousBuild
            ? " — issues from last full compile (this build reported 0/0)"
            : string.Empty;

        IssueSummaryText.Text = filter switch
        {
            IssueFilter.Errors => currentLogKind == BuildLogKind.Test
                ? FormatIssueCountLabel("failure", "failures", visibleIssues.Count, errorCount) + carryNote
                : FormatIssueCountLabel("error", "errors", visibleIssues.Count, errorCount) + carryNote,
            IssueFilter.Warnings => currentLogKind == BuildLogKind.Test
                ? FormatIssueCountLabel("skipped test", "skipped tests", visibleIssues.Count, warningCount) + carryNote
                : FormatIssueCountLabel("warning", "warnings", visibleIssues.Count, warningCount) + carryNote,
            _ => currentLogKind == BuildLogKind.Test
                ? $"{errorCount} failed, {warningCount} skipped{carryNote}"
                : $"{errorCount} errors, {warningCount} warnings{carryNote}",
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

    private IReadOnlyList<LogIssue> ParseIssuesForCurrentLog()
    {
        var logPath = currentRecord?.LogFilePath ?? logStore.GetLogPath(projectId, currentLogKind);
        return currentLogKind switch
        {
            BuildLogKind.Test => DotNetTestOutputParser.ParseIssues(currentLogText),
            BuildLogKind.Run => DotNetRunOutputParser.ParseIssues(currentLogText),
            _ => BuildLogParser.ResolveBuildIssues(currentLogText, logPath)
        };
    }

    private void RefreshResolvedIssueCounts(LiveBuildLogView? live = null)
    {
        var parsedErrors = allIssues.Count(i => i.IsError);
        var parsedWarnings = allIssues.Count(i => !i.IsError);

        if (currentLogKind is BuildLogKind.Test or BuildLogKind.Run)
        {
            issuesCarriedFromPreviousBuild = false;
            resolvedDisplayErrorCount = parsedErrors;
            resolvedDisplayWarningCount = parsedWarnings;
            return;
        }

        var logPath = currentRecord?.LogFilePath ?? logStore.GetLogPath(projectId, currentLogKind);
        var resolved = BuildIssueCountResolver.Resolve(currentLogText, logPath);
        var metaErrors = live?.ErrorCount ?? currentRecord?.ErrorCount ?? 0;
        var metaWarnings = live?.WarningCount ?? currentRecord?.WarningCount ?? 0;
        var note = BuildLogParser.TryParseIncrementalHealthNote(currentLogText);

        resolvedDisplayErrorCount = Math.Max(
            parsedErrors,
            Math.Max(metaErrors, Math.Max(note.Errors, resolved.Errors)));
        resolvedDisplayWarningCount = Math.Max(
            parsedWarnings,
            Math.Max(metaWarnings, Math.Max(note.Warnings, resolved.Warnings)));

        issuesCarriedFromPreviousBuild =
            IncrementalBuildDetector.WasCompileSkipped(currentLogText)
            && resolvedDisplayWarningCount + resolvedDisplayErrorCount > 0
            && BuildLogParser.ParseWarningCount(currentLogText) == 0
            && BuildLogParser.ParseErrorCount(currentLogText) == 0;
    }

    private string FormatFooterText(int errorCount, int warningCount, bool isLive)
    {
        var issueLabel = currentLogKind == BuildLogKind.Test
            ? $"{errorCount} failed | {warningCount} skipped"
            : $"{errorCount} errors | {warningCount} warnings";
        return isLive ? $"{issueLabel} | live output" : issueLabel;
    }

    private static string FormatIssueCountLabel(string singular, string plural, int shown, int total) =>
        shown == total
            ? $"{total} {((total == 1) ? singular : plural)} shown"
            : $"{shown} of {total} {((total == 1) ? singular : plural)} shown";

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

    private void UpdateErrorBanner(int errorCount)
    {
        if (ErrorBannerText is null)
        {
            return;
        }

        if (errorCount > 0)
        {
            var label = currentLogKind == BuildLogKind.Test ? "failures" : "errors";
            ErrorBannerText.Text = errorCount == 1
                ? $"1 {label.TrimEnd('s')} in this log — see list below"
                : $"{errorCount} {label} in this log — see list below";
            ErrorBannerText.Visibility = Visibility.Visible;
        }
        else
        {
            ErrorBannerText.Visibility = Visibility.Collapsed;
        }
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
                currentLogText,
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
        var errors = allIssues.Where(i => i.IsError).Select(i => i.Text).ToList();
        if (errors.Count == 0)
        {
            return;
        }

        WpfClipboard.SetText(string.Join(Environment.NewLine, errors));
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
