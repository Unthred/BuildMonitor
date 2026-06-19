using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.LocalBuild;
using BuildMonitor.Infrastructure.Services;
using BuildMonitor.TrayApp.Services;
using WpfButton = System.Windows.Controls.Button;
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
        ThemeService.ThemeChanged -= OnThemeChanged;
        WindowLayoutService.Capture(this, windowsLayoutStore.Layout.Diagnostics);
        _ = windowsLayoutStore.SaveAsync();
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
        var projectIds = snapshots.Select(s => s.ProjectId)
            .Concat(entries.Select(e => e.ProjectId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var order = snapshots
            .OrderBy(s => s.ProjectDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.ProjectId)
            .Concat(projectIds.Where(id => snapshots.All(s => !s.ProjectId.Equals(id, StringComparison.OrdinalIgnoreCase))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
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
                    new LocalProjectDefinition
                    {
                        Id = projectId,
                        DisplayName = displayName,
                        IsActiveInSession = false
                    },
                    new GlobalMonitorSettings(),
                    new FileChangeBurstStats());

                tab.RefreshTriggers(entries, unexpectedOnly, journal);
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
        if (sender is WpfButton { Tag: BuildTriggerRowViewModel row })
        {
            journal.SetVerdict(row.Record.Id, BuildTriggerVerdict.Unexpected);
        }
    }

    private void ClearVerdictClicked(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: BuildTriggerRowViewModel row })
        {
            journal.SetVerdict(row.Record.Id, BuildTriggerVerdict.Unreviewed);
        }
    }

    private void UserNoteLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox { Tag: BuildTriggerRowViewModel row })
        {
            row.SaveUserNote();
        }
    }

    private void FilterChanged(object sender, RoutedEventArgs e) => RefreshAll();

    private void RefreshClicked(object sender, RoutedEventArgs e) => RefreshAll();

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    private sealed class ProjectDiagnosticsTabViewModel : INotifyPropertyChanged
    {
        private string displayName;
        private BuildIntelligenceSnapshot? intelligence;

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

        public ObservableCollection<BuildTriggerRowViewModel> Triggers { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void RefreshTriggers(
            IReadOnlyList<BuildTriggerRecord> entries,
            bool unexpectedOnly,
            BuildTriggerJournal triggerJournal)
        {
            Triggers.Clear();
            foreach (var entry in entries.Where(e =>
                         e.ProjectId.Equals(ProjectId, StringComparison.OrdinalIgnoreCase)))
            {
                if (unexpectedOnly && entry.Verdict != BuildTriggerVerdict.Unexpected)
                {
                    continue;
                }

                Triggers.Add(new BuildTriggerRowViewModel(entry, triggerJournal));
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class BuildTriggerRowViewModel : INotifyPropertyChanged
    {
        private readonly BuildTriggerJournal journal;
        private string userNote;

        public BuildTriggerRowViewModel(BuildTriggerRecord record, BuildTriggerJournal journal)
        {
            Record = record;
            this.journal = journal;
            userNote = record.UserNote ?? string.Empty;
        }

        public BuildTriggerRecord Record { get; }

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

        public void SaveUserNote() => journal.SetUserNote(Record.Id, userNote);

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
