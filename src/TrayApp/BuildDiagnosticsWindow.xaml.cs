using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using BuildMonitor.Core.Models;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.TrayApp.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace BuildMonitor.TrayApp;

public partial class BuildDiagnosticsWindow : Window
{
    private readonly BuildTriggerJournal journal;
    private readonly AppWindowsLayoutStore windowsLayoutStore;
    private readonly ObservableCollection<BuildTriggerRowViewModel> rows = [];

    public BuildDiagnosticsWindow(BuildTriggerJournal journal, AppWindowsLayoutStore windowsLayoutStore)
    {
        this.journal = journal;
        this.windowsLayoutStore = windowsLayoutStore;
        InitializeComponent();
        TriggersGrid.ItemsSource = rows;
        journal.Changed += OnJournalChanged;
        Closed += OnClosed;
        Loaded += (_, _) =>
        {
            ApplyTheme(ThemeService.CurrentResolved);
            WindowLayoutService.Apply(this, windowsLayoutStore.Layout.Diagnostics, 1100, 640);
            if (double.IsNaN(windowsLayoutStore.Layout.Diagnostics.Left))
            {
                TrayScreenPlacement.PlaceWindowCentered(this);
            }
        };
        ThemeService.ThemeChanged += OnThemeChanged;
        AppIconService.ApplyToWindow(this);
        ReloadProjectFilter();
        RefreshRows();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
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

    private void OnJournalChanged() =>
        Dispatcher.BeginInvoke(RefreshRows);

    private void ReloadProjectFilter()
    {
        var selected = ProjectFilterCombo.SelectedItem as string;
        var projects = journal.GetEntries()
            .Select(e => e.ProjectDisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ProjectFilterCombo.ItemsSource = new[] { "(All projects)" }.Concat(projects).ToList();
        ProjectFilterCombo.SelectedItem = projects.Contains(selected ?? string.Empty) ? selected : "(All projects)";
    }

    private void RefreshRows()
    {
        ReloadProjectFilter();
        var projectFilter = ProjectFilterCombo.SelectedItem as string;
        var unexpectedOnly = UnexpectedOnlyCheck.IsChecked == true;

        rows.Clear();
        foreach (var entry in journal.GetEntries())
        {
            if (unexpectedOnly && entry.Verdict != BuildTriggerVerdict.Unexpected)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(projectFilter)
                && projectFilter != "(All projects)"
                && !entry.ProjectDisplayName.Equals(projectFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rows.Add(new BuildTriggerRowViewModel(entry, journal));
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

    private void FilterChanged(object sender, RoutedEventArgs e) => RefreshRows();

    private void RefreshClicked(object sender, RoutedEventArgs e) => RefreshRows();

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

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

        public string ProjectDisplayName => Record.ProjectDisplayName;

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
