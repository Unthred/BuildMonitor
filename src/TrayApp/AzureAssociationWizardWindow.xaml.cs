using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.AzureDevOps;

namespace BuildMonitor.TrayApp;

public partial class AzureAssociationWizardWindow : Window
{
    private readonly AzureAssociationCoordinator coordinator;
    private readonly ObservableCollection<PipelineRow> pipelineRows = [];
    private bool suppressSelection;

    public AzureDevOpsProjectAttachment? ResultAttachment { get; private set; }

    public AzureAssociationWizardWindow(AzureAssociationCoordinator coordinator, string title, string subtitle)
    {
        this.coordinator = coordinator;
        InitializeComponent();
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        PipelinesList.ItemsSource = pipelineRows;
        Loaded += async (_, _) => await RefreshFromCoordinatorAsync(initial: true);
    }

    private async Task RefreshFromCoordinatorAsync(bool initial)
    {
        StatusText.Text = coordinator.StatusMessage ?? (coordinator.IsBusy ? "Loading…" : string.Empty);
        suppressSelection = true;
        try
        {
            ProjectsList.ItemsSource = coordinator.Projects;
            if (coordinator.SelectedProject is not null)
            {
                ProjectsList.SelectedItem = coordinator.SelectedProject;
            }

            ReposList.ItemsSource = coordinator.Repositories;
            if (coordinator.SelectedRepository is not null)
            {
                ReposList.SelectedItem = coordinator.SelectedRepository;
            }

            RebuildPipelineRows();
            UpdateSuggestionUi();
        }
        finally
        {
            suppressSelection = false;
        }

        if (initial && coordinator.SuggestedMatch is not null && coordinator.SelectedRepository is null)
        {
            // leave suggestion visible for user confirmation
        }
    }

    private void RebuildPipelineRows()
    {
        pipelineRows.Clear();
        foreach (var p in coordinator.Pipelines)
        {
            pipelineRows.Add(new PipelineRow
            {
                DefinitionId = p.DefinitionId,
                Label = p.IsEnabled ? p.DisplayName : $"{p.DisplayName} (disabled)",
                IsSelected = coordinator.SelectedPipelineIds.Contains(p.DefinitionId)
            });
        }
    }

    private void UpdateSuggestionUi()
    {
        if (coordinator.SuggestedMatch is null)
        {
            SuggestionText.Visibility = Visibility.Collapsed;
            ApplySuggestionButton.Visibility = Visibility.Collapsed;
            return;
        }

        SuggestionText.Text =
            $"Suggested from local Git remote: {coordinator.SuggestedMatch.ProjectName} / {coordinator.SuggestedMatch.RepositoryName} ({coordinator.SuggestedMatch.MatchReason}). Confirm before finishing.";
        SuggestionText.Visibility = Visibility.Visible;
        ApplySuggestionButton.Visibility = Visibility.Visible;
    }

    private async void ProjectsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressSelection || ProjectsList.SelectedItem is not AzureProjectSummary project)
        {
            return;
        }

        StatusText.Text = "Loading repositories…";
        await coordinator.SelectProjectAsync(project, CancellationToken.None);
        await RefreshFromCoordinatorAsync(initial: false);
    }

    private async void ReposList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressSelection || ReposList.SelectedItem is not AzureRepositorySummary repo)
        {
            return;
        }

        StatusText.Text = "Loading pipelines…";
        await coordinator.SelectRepositoryAsync(repo, CancellationToken.None);
        await RefreshFromCoordinatorAsync(initial: false);
    }

    private void PipelineCheckChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox { DataContext: PipelineRow row })
        {
            return;
        }

        coordinator.SetPipelineSelected(row.DefinitionId, row.IsSelected);
    }

    private async void ApplySuggestionClicked(object sender, RoutedEventArgs e)
    {
        if (coordinator.SuggestedMatch is null)
        {
            return;
        }

        var project = coordinator.Projects.FirstOrDefault(p =>
            string.Equals(p.Id, coordinator.SuggestedMatch.ProjectId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Name, coordinator.SuggestedMatch.ProjectName, StringComparison.OrdinalIgnoreCase));
        if (project is null)
        {
            return;
        }

        StatusText.Text = "Applying suggestion…";
        await coordinator.SelectProjectAsync(project, CancellationToken.None);
        var repo = coordinator.Repositories.FirstOrDefault(r =>
            string.Equals(r.Id, coordinator.SuggestedMatch.RepositoryId, StringComparison.OrdinalIgnoreCase));
        if (repo is not null)
        {
            await coordinator.SelectRepositoryAsync(repo, CancellationToken.None);
        }

        await RefreshFromCoordinatorAsync(initial: false);
    }

    private void CancelClicked(object sender, RoutedEventArgs e)
    {
        ResultAttachment = null;
        DialogResult = false;
        Close();
    }

    private void FinishClicked(object sender, RoutedEventArgs e)
    {
        var attachment = coordinator.BuildAttachment();
        if (attachment is null)
        {
            StatusText.Text = coordinator.StatusMessage ?? "Select a project and repository.";
            return;
        }

        ResultAttachment = attachment;
        DialogResult = true;
        Close();
    }

    private sealed class PipelineRow : INotifyPropertyChanged
    {
        private bool isSelected;

        public int DefinitionId { get; init; }
        public string Label { get; init; } = string.Empty;

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                {
                    return;
                }

                isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
