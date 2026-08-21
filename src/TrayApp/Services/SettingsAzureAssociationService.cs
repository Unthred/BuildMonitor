using System.IO;
using System.Windows;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.AzureDevOps;
using BuildMonitor.Infrastructure.Git;
using BuildMonitor.Infrastructure.Security;
using Microsoft.Win32;

namespace BuildMonitor.TrayApp.Services;

/// <summary>Settings Projects-tab Azure association actions (keeps SettingsWindow thinner).</summary>
public sealed class SettingsAzureAssociationService(
    Window owner,
    AppSettings settings,
    AzureDevOpsDiscoveryClient discoveryClient,
    AzureConnectionSecretStore secretStore,
    LocalGitContextReader gitContextReader)
{
    public async Task<MonitoredProjectSettings?> TryAddFromAzureAsync()
    {
        var attachment = await RunWizardAsync(
            AzureAssociationMode.AddFromAzure,
            "Add from Azure DevOps",
            "Create an Azure-only BuildMonitor project. Continuous monitoring is not enabled yet.",
            localRoot: null,
            existing: null);
        return attachment is null ? null : AzureAssociationCoordinator.CreateAzureOnlyProject(attachment);
    }

    public async Task<bool> TryAttachAsync(MonitoredProjectSettings project)
    {
        if (project.Local is null || project.Azure is not null)
        {
            return false;
        }

        var attachment = await RunWizardAsync(
            AzureAssociationMode.AttachToExisting,
            "Attach Azure DevOps",
            "Associate a repository with this local project. Local settings are preserved.",
            project.Local.RootFolder,
            existing: null);
        if (attachment is null)
        {
            return false;
        }

        AzureAssociationCoordinator.AttachAzure(project, attachment);
        return true;
    }

    public async Task<bool> TryChangeAsync(MonitoredProjectSettings project)
    {
        if (project.Azure is null)
        {
            return false;
        }

        var attachment = await RunWizardAsync(
            AzureAssociationMode.ChangeExisting,
            "Change Azure association",
            "Replace the Azure repository/pipeline selection for this project.",
            project.Local?.RootFolder,
            project.Azure);
        if (attachment is null)
        {
            return false;
        }

        AzureAssociationCoordinator.ChangeAzure(project, attachment);
        return true;
    }

    public bool TryDetach(MonitoredProjectSettings project, out string? error) =>
        AzureAssociationCoordinator.TryDetachAzure(project, out error);

    public bool TryAssociateLocalFolder(MonitoredProjectSettings project)
    {
        if (project.Local is not null)
        {
            return false;
        }

        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        project.Local = new LocalProjectAttachment { RootFolder = dialog.FolderName };
        if (string.IsNullOrWhiteSpace(project.DisplayName)
            || project.DisplayName.Equals("Azure project", StringComparison.OrdinalIgnoreCase))
        {
            project.DisplayName = Path.GetFileName(dialog.FolderName.TrimEnd('\\', '/'));
        }

        return true;
    }

    private async Task<AzureDevOpsProjectAttachment?> RunWizardAsync(
        AzureAssociationMode mode,
        string title,
        string subtitle,
        string? localRoot,
        AzureDevOpsProjectAttachment? existing)
    {
        var connection = settings.Connections.FirstOrDefault();
        if (connection is null || string.IsNullOrWhiteSpace(connection.OrganizationUrl))
        {
            System.Windows.MessageBox.Show(
                owner,
                "Configure an Azure DevOps connection on the Azure tab first.",
                "Azure connection required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return null;
        }

        var coordinator = new AzureAssociationCoordinator(
            discoveryClient,
            (id, ct) => secretStore.LoadAsync(id, ct),
            gitContextReader);

        var ok = await coordinator.InitializeAsync(mode, connection, CancellationToken.None, localRoot, existing);
        if (!ok && !string.IsNullOrWhiteSpace(coordinator.StatusMessage))
        {
            System.Windows.MessageBox.Show(
                owner,
                coordinator.StatusMessage,
                "Azure discovery",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return null;
        }

        var wizard = new AzureAssociationWizardWindow(coordinator, title, subtitle) { Owner = owner };
        return wizard.ShowDialog() == true ? wizard.ResultAttachment : null;
    }
}
