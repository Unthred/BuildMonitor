using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;
using BuildMonitor.Infrastructure.Services;
using BuildMonitor.TrayApp.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace BuildMonitor.TrayApp;

public partial class BuildDiagnosticsWindow : Window
{
    private readonly BuildTriggerJournal journal;
    private readonly ProjectOrchestrator orchestrator;
    private readonly AppWindowsLayoutStore windowsLayoutStore;
    private readonly ObservableCollection<ProjectDiagnosticsTabViewModel> projectTabs = [];
    private readonly DispatcherTimer intelligenceRefreshTimer;
    private string? selectedProjectId;
    private bool suppressSelectionTracking;
    private bool noteEditorFocused;
    private bool refreshTriggersAfterNoteEdit;
    private WpfDataGrid? activeTriggersGrid;
    private DispatcherTimer? columnLayoutSaveTimer;

    public BuildDiagnosticsWindow(
        BuildTriggerJournal journal,
        ProjectOrchestrator orchestrator,
        AppWindowsLayoutStore windowsLayoutStore)
    {
        this.journal = journal;
        this.orchestrator = orchestrator;
        this.windowsLayoutStore = windowsLayoutStore;
        InitializeComponent();
        ProjectTabs.ItemsSource = projectTabs;
        ProjectTabs.SelectionChanged += (_, _) =>
        {
            if (suppressSelectionTracking)
            {
                return;
            }

            if (ProjectTabs.SelectedItem is ProjectDiagnosticsTabViewModel tab)
            {
                selectedProjectId = tab.ProjectId;
            }
        };

        intelligenceRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        intelligenceRefreshTimer.Tick += (_, _) => RefreshAll();

        journal.Changed += OnJournalChanged;
        orchestrator.ControlPlaneEventJournal.Changed += OnJournalChanged;
        Closed += OnClosed;
        Loaded += OnLoaded;
        ThemeService.ThemeChanged += OnThemeChanged;
        RefreshAll();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyTheme(ThemeService.CurrentResolved);
        WindowLayoutService.Apply(this, windowsLayoutStore.Layout.Diagnostics, 1100, 720);
        if (double.IsNaN(windowsLayoutStore.Layout.Diagnostics.Left))
        {
            TrayScreenPlacement.PlaceWindowCentered(this);
        }

        AppIconService.ApplyToWindow(this);
        intelligenceRefreshTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        intelligenceRefreshTimer.Stop();
        journal.Changed -= OnJournalChanged;
        orchestrator.ControlPlaneEventJournal.Changed -= OnJournalChanged;
        ThemeService.ThemeChanged -= OnThemeChanged;
        CaptureTriggerGridColumnWidths();
        WindowLayoutService.Capture(this, windowsLayoutStore.Layout.Diagnostics);
        _ = windowsLayoutStore.SaveAsync();
    }

    private void TriggersGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfDataGrid grid)
        {
            return;
        }

        activeTriggersGrid = grid;
        DiagnosticsGridLayoutService.ApplyColumnWidths(
            grid,
            windowsLayoutStore.Layout.Diagnostics.TriggerGridColumnWidths);
    }

    private void TriggersGridLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is not WpfDataGrid grid || !grid.IsLoaded)
        {
            return;
        }

        columnLayoutSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        columnLayoutSaveTimer.Tick -= ColumnLayoutSaveTick;
        columnLayoutSaveTimer.Tick += ColumnLayoutSaveTick;
        columnLayoutSaveTimer.Stop();
        columnLayoutSaveTimer.Start();
    }

    private void ColumnLayoutSaveTick(object? sender, EventArgs e)
    {
        columnLayoutSaveTimer?.Stop();
        CaptureTriggerGridColumnWidths();
        _ = windowsLayoutStore.SaveAsync();
    }

    private void CaptureTriggerGridColumnWidths()
    {
        if (activeTriggersGrid is null)
        {
            return;
        }

        windowsLayoutStore.Layout.Diagnostics.TriggerGridColumnWidths =
            DiagnosticsGridLayoutService.CaptureColumnWidths(activeTriggersGrid);
    }

    private void OnThemeChanged(ResolvedTheme theme) => ApplyTheme(theme);

    private void ApplyTheme(ResolvedTheme theme)
    {
        ThemeService.ApplyToWindow(this, theme);
        ThemeService.ApplyChrome(this, theme == ResolvedTheme.Dark);
    }

    private void OnJournalChanged() => Dispatcher.BeginInvoke(RefreshAll);

    private void RefreshAll()
    {
        var preserveProjectId = selectedProjectId
            ?? (ProjectTabs.SelectedItem as ProjectDiagnosticsTabViewModel)?.ProjectId;

        var snapshots = orchestrator.GetBuildIntelligenceSnapshots();
        var unexpectedOnly = UnexpectedOnlyCheck.IsChecked == true;
        var entries = journal.GetEntries();
        var order = snapshots
            .OrderBy(s => s.ProjectDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.ProjectId)
            .ToList();

        suppressSelectionTracking = true;
        try
        {
            foreach (var stale in projectTabs.Where(t => !order.Contains(t.ProjectId, StringComparer.OrdinalIgnoreCase)).ToList())
            {
                projectTabs.Remove(stale);
            }

            for (var index = 0; index < order.Count; index++)
            {
                var projectId = order[index];
                var snapshot = snapshots.FirstOrDefault(s =>
                    s.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase));
                var displayName = snapshot?.ProjectDisplayName
                    ?? entries.FirstOrDefault(e => e.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase))?.ProjectDisplayName
                    ?? projectId;

                var tab = projectTabs.FirstOrDefault(t =>
                    t.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase));
                if (tab is null)
                {
                    tab = new ProjectDiagnosticsTabViewModel(projectId, displayName);
                    projectTabs.Insert(Math.Min(index, projectTabs.Count), tab);
                }
                else
                {
                    tab.DisplayName = displayName;
                    var currentIndex = projectTabs.IndexOf(tab);
                    if (currentIndex != index)
                    {
                        projectTabs.Move(currentIndex, index);
                    }
                }

                tab.Intelligence = snapshot ?? BuildIntelligenceSnapshot.FromStoredStats(
                    new MonitoredProjectSettings
                    {
                        Id = projectId,
                        DisplayName = displayName,
                        IsActiveInSession = false
                    },
                    new GlobalMonitorSettings(),
                    new FileChangeBurstStats());
                tab.ControlPlaneMetrics = orchestrator.GetControlPlaneMetrics(projectId);
                tab.ControlPlaneWorkflow = orchestrator.GetControlPlaneWorkflow(projectId);

                if (!IsNoteEditorFocused())
                {
                    tab.RefreshTriggers(entries, unexpectedOnly, journal);
                }
                else
                {
                    refreshTriggersAfterNoteEdit = true;
                }
            }

            var hasProjects = projectTabs.Count > 0;
            ProjectTabs.Visibility = hasProjects ? Visibility.Visible : Visibility.Collapsed;
            EmptyProjectsText.Visibility = hasProjects ? Visibility.Collapsed : Visibility.Visible;
            IntelligenceUpdatedText.Text = $"Updated {DateTime.Now:t}";

            if (hasProjects)
            {
                var target = !string.IsNullOrWhiteSpace(preserveProjectId)
                    ? projectTabs.FirstOrDefault(t =>
                        t.ProjectId.Equals(preserveProjectId, StringComparison.OrdinalIgnoreCase))
                    : null;
                target ??= ProjectTabs.SelectedItem as ProjectDiagnosticsTabViewModel ?? projectTabs.FirstOrDefault();

                if (target is not null && !ReferenceEquals(ProjectTabs.SelectedItem, target))
                {
                    ProjectTabs.SelectedItem = target;
                }

                selectedProjectId = target?.ProjectId;
            }
        }
        finally
        {
            suppressSelectionTracking = false;
        }
    }

    private void ExpectedClicked(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: BuildTriggerRowViewModel row })
        {
            journal.SetVerdict(row.Record.Id, BuildTriggerVerdict.Expected);
        }
    }

    private void UnexpectedClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: BuildTriggerRowViewModel row })
        {
            return;
        }

        journal.SetVerdict(row.Record.Id, BuildTriggerVerdict.Unexpected);
        var training = orchestrator.ProcessUnexpectedVerdict(row.Record with { Verdict = BuildTriggerVerdict.Unexpected });
        if (training.SuggestedExcludeSegments.Count == 0)
        {
            return;
        }

        var segmentLines = string.Join(
            Environment.NewLine,
            training.SuggestedExcludeSegments.Select(s => $"  • {s}"));
        var message =
            $"Add these folders to the watch ignore list for {row.Record.ProjectDisplayName}?"
            + $"{Environment.NewLine}{Environment.NewLine}{segmentLines}"
            + $"{Environment.NewLine}{Environment.NewLine}Future saves under these folders will not trigger rebuilds."
            + $"{Environment.NewLine}(Saved in build-training.json — no app recompile needed.)";

        var add = System.Windows.MessageBox.Show(
            this,
            message,
            "Train Build Monitor",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (add == MessageBoxResult.Yes)
        {
            orchestrator.ApplyLearnedExcludeSegments(row.Record.ProjectId, training.SuggestedExcludeSegments);
        }
    }

    private void ClearVerdictClicked(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: BuildTriggerRowViewModel row })
        {
            journal.SetVerdict(row.Record.Id, BuildTriggerVerdict.Unreviewed);
        }
    }

    private void UserNoteGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox { Tag: BuildTriggerRowViewModel })
        {
            noteEditorFocused = true;
        }
    }

    private void UserNoteLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfTextBox { Tag: BuildTriggerRowViewModel row })
        {
            return;
        }

        row.SaveUserNote();
        noteEditorFocused = false;

        if (!refreshTriggersAfterNoteEdit)
        {
            return;
        }

        refreshTriggersAfterNoteEdit = false;
        RefreshAll();
    }

    private bool IsNoteEditorFocused()
    {
        if (noteEditorFocused)
        {
            return true;
        }

        return Keyboard.FocusedElement is WpfTextBox { Tag: BuildTriggerRowViewModel };
    }

    private void FilterChanged(object sender, RoutedEventArgs e) => RefreshAll();

    private void RefreshClicked(object sender, RoutedEventArgs e) => RefreshAll();

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    private sealed class ProjectDiagnosticsTabViewModel : INotifyPropertyChanged
    {
        private string displayName;
        private BuildIntelligenceSnapshot? intelligence;
        private ControlPlaneMetricsSnapshot? controlPlaneMetrics;
        private ControlPlaneWorkflowSnapshot? controlPlaneWorkflow;

        public ProjectDiagnosticsTabViewModel(string projectId, string displayName)
        {
            ProjectId = projectId;
            this.displayName = displayName;
            Triggers = [];
        }

        public string ProjectId { get; }

        public string DisplayName
        {
            get => displayName;
            set
            {
                if (displayName == value)
                {
                    return;
                }

                displayName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TabTitle));
            }
        }

        public string TabTitle => intelligence?.TabTitle ?? DisplayName;

        public BuildIntelligenceSnapshot? Intelligence
        {
            get => intelligence;
            set
            {
                intelligence = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TabTitle));
            }
        }

        public ControlPlaneMetricsSnapshot? ControlPlaneMetrics
        {
            get => controlPlaneMetrics;
            set
            {
                controlPlaneMetrics = value;
                OnPropertyChanged();
            }
        }

        public ControlPlaneWorkflowSnapshot? ControlPlaneWorkflow
        {
            get => controlPlaneWorkflow;
            set
            {
                controlPlaneWorkflow = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<BuildTriggerRowViewModel> Triggers { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void RefreshTriggers(
            IReadOnlyList<BuildTriggerRecord> entries,
            bool unexpectedOnly,
            BuildTriggerJournal triggerJournal)
        {
            var filtered = entries
                .Where(e => e.ProjectId.Equals(ProjectId, StringComparison.OrdinalIgnoreCase))
                .Where(e => !unexpectedOnly || e.Verdict == BuildTriggerVerdict.Unexpected)
                .ToList();

            var desiredIds = new HashSet<string>(
                filtered.Select(e => e.Id),
                StringComparer.OrdinalIgnoreCase);

            for (var i = Triggers.Count - 1; i >= 0; i--)
            {
                if (!desiredIds.Contains(Triggers[i].Record.Id))
                {
                    Triggers.RemoveAt(i);
                }
            }

            for (var i = 0; i < filtered.Count; i++)
            {
                var entry = filtered[i];
                var existing = Triggers.FirstOrDefault(t =>
                    t.Record.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    Triggers.Insert(i, new BuildTriggerRowViewModel(entry, triggerJournal));
                    continue;
                }

                existing.SyncFrom(entry);
                var currentIndex = Triggers.IndexOf(existing);
                if (currentIndex != i)
                {
                    Triggers.Move(currentIndex, i);
                }
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class BuildTriggerRowViewModel : INotifyPropertyChanged
    {
        private readonly BuildTriggerJournal journal;
        private BuildTriggerRecord record;
        private string userNote;

        public BuildTriggerRowViewModel(BuildTriggerRecord record, BuildTriggerJournal journal)
        {
            this.record = record;
            this.journal = journal;
            userNote = record.UserNote ?? string.Empty;
        }

        public BuildTriggerRecord Record => record;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string WhenLocal => BuildTimestampFormatter.FormatLocalShort(Record.OccurredAtUtc);

        public string KindLabel => BuildTriggerKindFormatter.ToLabel(Record.Kind);

        public string Summary => Record.Summary;

        public string InferredCause => string.IsNullOrWhiteSpace(Record.InferredCause)
            ? "—"
            : Record.InferredCause;

        public string Detail => string.IsNullOrWhiteSpace(Record.Detail) ? "—" : Record.Detail;

        public string ChangedPathsText =>
            Record.ChangedPaths is { Count: > 0 }
                ? string.Join(
                    "; ",
                    Record.ChangedPaths.Take(4))
                  + (Record.ChangedPaths.Count > 4 ? $" (+{Record.ChangedPaths.Count - 4} more)" : string.Empty)
                : "—";

        public string VerdictLabel => Record.Verdict switch
        {
            BuildTriggerVerdict.Expected => "Expected",
            BuildTriggerVerdict.Unexpected => "Unexpected",
            _ => "—"
        };

        public string UserNote
        {
            get => userNote;
            set
            {
                if (userNote == value)
                {
                    return;
                }

                userNote = value;
                OnPropertyChanged();
            }
        }

        public void SaveUserNote() => journal.SetUserNote(record.Id, userNote);

        public void SyncFrom(BuildTriggerRecord latest)
        {
            if (record.Id.Equals(latest.Id, StringComparison.OrdinalIgnoreCase)
                && record.Verdict == latest.Verdict
                && string.Equals(record.UserNote, latest.UserNote, StringComparison.Ordinal)
                && string.Equals(record.Summary, latest.Summary, StringComparison.Ordinal)
                && string.Equals(record.Detail, latest.Detail, StringComparison.Ordinal)
                && string.Equals(record.InferredCause, latest.InferredCause, StringComparison.Ordinal)
                && record.OccurredAtUtc == latest.OccurredAtUtc
                && PathsEqual(record.ChangedPaths, latest.ChangedPaths))
            {
                return;
            }

            record = latest;
            userNote = latest.UserNote ?? string.Empty;
            OnPropertyChanged(nameof(WhenLocal));
            OnPropertyChanged(nameof(KindLabel));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(InferredCause));
            OnPropertyChanged(nameof(Detail));
            OnPropertyChanged(nameof(ChangedPathsText));
            OnPropertyChanged(nameof(VerdictLabel));
            OnPropertyChanged(nameof(UserNote));
        }

        private static bool PathsEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
        {
            if (left is null || left.Count == 0)
            {
                return right is null || right.Count == 0;
            }

            if (right is null || left.Count != right.Count)
            {
                return false;
            }

            for (var i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
