using System.Collections.ObjectModel;
using System.Windows;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.TrayApp.Services;

/// <summary>Binds in-memory operational history into diagnostics list rows (#116).</summary>
internal static class OperationalHistoryDiagnosticsBinder
{
    public static void Refresh(
        ObservableCollection<OperationalHistoryDiagnosticsRow> target,
        IOperationalHistoryStore? store,
        string projectId,
        out string statusMessage)
    {
        target.Clear();
        if (store is null)
        {
            statusMessage = "Recent activity unavailable";
            return;
        }

        var events = store.GetRecentForProject(
            projectId,
            OperationalHistoryPresentationMapper.DiagnosticsRowLimit);
        var section = OperationalHistoryPresentationMapper.BuildSection(
            storeAvailable: true,
            events,
            OperationalHistoryPresentationMapper.DiagnosticsRowLimit,
            expandByDefault: true,
            DateTimeOffset.UtcNow);

        if (section.Availability == OperationalHistoryAvailability.Empty)
        {
            statusMessage = section.EmptyMessage;
            return;
        }

        statusMessage = string.Empty;
        foreach (var row in section.Rows)
        {
            target.Add(new OperationalHistoryDiagnosticsRow(row));
        }
    }
}

internal sealed class OperationalHistoryDiagnosticsRow(OperationalHistoryRowPresentation row)
{
    public string TimeLabel => row.TimeLabel;
    public string SourceLabel => $"{row.SourceGlyph} {row.SourceLabel}";
    public string PrimaryText => row.PrimaryText;
    public string SecondaryText => row.SecondaryText ?? string.Empty;
    public string DetailText => row.DetailText ?? string.Empty;
    public string ToolTip => row.ToolTip;
    public bool HasDetail => !string.IsNullOrWhiteSpace(row.DetailText);
    public Visibility SecondaryVisibility =>
        string.IsNullOrWhiteSpace(row.SecondaryText) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DetailVisibility =>
        string.IsNullOrWhiteSpace(row.DetailText) ? Visibility.Collapsed : Visibility.Visible;
}
