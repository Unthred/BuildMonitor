using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using BuildMonitor.Infrastructure.Diagnostics;
using BuildMonitor.Infrastructure.Services;
using BuildMonitor.TrayApp.Services;

namespace BuildMonitor.TrayApp;

public partial class BuildMonitorHealthWindow : Window
{
    private static readonly string[] CoreActivityWorkerIds =
    [
        "ui.dispatcher",
        "ui.health-callback",
        "health.coalescer"
    ];

    private readonly AppWindowsLayoutStore windowsLayoutStore;
    private readonly ProjectOrchestrator orchestrator;
    private readonly ObservableCollection<WorkerHealthRowViewModel> rows = [];
    private readonly ObservableCollection<CurrentActivityItem> currentActions = [];
    private readonly Dictionary<string, WorkerHealthRowViewModel> rowById = new(StringComparer.Ordinal);
    private readonly DispatcherTimer refreshTimer;
    private readonly DispatcherTimer resizeSettleTimer;
    private readonly TaskCompletionSource initialLoadTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool refreshPausedForResize;
    private bool? lastHasActivity;
    private string baseFooterText = string.Empty;
    private string identityFooterText = string.Empty;
    private string? lastControlPlaneFooter;

    public Task WaitForInitialLoadAsync() => initialLoadTcs.Task;

    public BuildMonitorHealthWindow(AppWindowsLayoutStore windowsLayoutStore, ProjectOrchestrator orchestrator)
    {
        this.windowsLayoutStore = windowsLayoutStore;
        this.orchestrator = orchestrator;
        InitializeComponent();
        WorkersGrid.ItemsSource = rows;
        CurrentActionsList.ItemsSource = currentActions;

        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        refreshTimer.Tick += (_, _) => RefreshRows();

        resizeSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        resizeSettleTimer.Tick += OnResizeSettled;

        Loaded += OnLoaded;
        Closed += OnClosed;
        SizeChanged += OnSizeChanged;
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyTheme(ThemeService.CurrentResolved);
        WindowLayoutService.Apply(this, windowsLayoutStore.Layout.BuildMonitorHealth, 980, 520);
        if (double.IsNaN(windowsLayoutStore.Layout.BuildMonitorHealth.Left))
        {
            TrayScreenPlacement.PlaceWindowCentered(this);
        }

        baseFooterText = BuildIdentityFooterText.Text;
        identityFooterText = BuildIdentityProvider.FormatFooterText();
        BuildIdentityFooterText.Text = string.IsNullOrWhiteSpace(identityFooterText)
            ? baseFooterText
            : $"{identityFooterText} | {baseFooterText}";

        RefreshRows();
        refreshTimer.Start();
        initialLoadTcs.TrySetResult();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        refreshTimer.Stop();
        resizeSettleTimer.Stop();
        ThemeService.ThemeChanged -= OnThemeChanged;
        SizeChanged -= OnSizeChanged;
        WindowLayoutService.Capture(this, windowsLayoutStore.Layout.BuildMonitorHealth);
        _ = windowsLayoutStore.SaveAsync();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        refreshPausedForResize = true;
        resizeSettleTimer.Stop();
        resizeSettleTimer.Start();
    }

    private void OnResizeSettled(object? sender, EventArgs e)
    {
        resizeSettleTimer.Stop();
        refreshPausedForResize = false;
        RefreshRows();
    }

    private void OnThemeChanged(ResolvedTheme theme) => ApplyTheme(theme);

    private void ApplyTheme(ResolvedTheme theme)
    {
        ThemeService.ApplyToWindow(this, theme);
        ThemeService.ApplyChrome(this, theme == ResolvedTheme.Dark);
        AppIconService.ApplyToWindow(this);
    }

    private void RefreshNowClicked(object sender, RoutedEventArgs e) => RefreshRows();

    private void RefreshRows()
    {
        if (refreshPausedForResize)
        {
            return;
        }

        var snapshots = WorkerHealthRegistry.Shared.GetSnapshots();
        SyncWorkerRows(snapshots);

        var dispatcher = snapshots.FirstOrDefault(s => s.Id == "ui.dispatcher");
        var uiCallback = snapshots.FirstOrDefault(s => s.Id == "ui.health-callback");
        var threadPoolPending = ThreadPool.PendingWorkItemCount;
        var threadPoolThreads = ThreadPool.ThreadCount;

        var summary =
            $"Thread pool: {threadPoolThreads} threads, {threadPoolPending} pending work items"
            + (dispatcher is null
                ? string.Empty
                : $" | UI dispatcher: {dispatcher.State}, last ping {FormatAge(dispatcher.Age)}")
            + (uiCallback?.LastWorkDurationMs is long ms
                ? $" | Last tray health UI pass: {ms} ms"
                : string.Empty);

        if (!string.Equals(SummaryText.Text, summary, StringComparison.Ordinal))
        {
            SummaryText.Text = summary;
        }

        SummaryText.ToolTip = summary;

        var desiredActions = BuildCurrentActivityItems(snapshots);

        SyncCurrentActions(desiredActions);

        UpdateControlPlaneFooter();
    }

    private void UpdateControlPlaneFooter()
    {
        // Surface “AI is starting/stopping monitoring via API” so we can confirm
        // whether /session/busy and /session/idle are being honored.
        var active = orchestrator
            .ListControlPlaneProjects()
            .Where(p => p.IsActiveInSession)
            .ToList();

        if (active.Count == 0)
        {
            lastControlPlaneFooter = null;
            BuildIdentityFooterText.Text = string.IsNullOrWhiteSpace(identityFooterText)
                ? baseFooterText
                : $"{identityFooterText} | {baseFooterText}";
            return;
        }

        var parts = new List<string>(Math.Min(active.Count, 3));
        foreach (var p in active.Take(3))
        {
            var metrics = orchestrator.GetControlPlaneMetrics(p.Id);
            parts.Add($"{p.DisplayName}: {metrics.SessionStateText}");
        }

        var extra = active.Count > 3 ? $" (+{active.Count - 3} more)" : string.Empty;
        var controlPlaneFooter = $"AI session: {string.Join(" · ", parts)}{extra}";

        if (string.Equals(controlPlaneFooter, lastControlPlaneFooter, StringComparison.Ordinal))
        {
            return;
        }

        lastControlPlaneFooter = controlPlaneFooter;
        BuildIdentityFooterText.Text = string.IsNullOrWhiteSpace(identityFooterText)
            ? $"{baseFooterText} | {controlPlaneFooter}"
            : $"{identityFooterText} | {baseFooterText} | {controlPlaneFooter}";
    }

    private static List<CurrentActivityItem> BuildCurrentActivityItems(IReadOnlyList<WorkerHealthSnapshot> snapshots)
    {
        var byId = snapshots.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var items = new List<CurrentActivityItem>(CoreActivityWorkerIds.Length + 4);

        foreach (var workerId in CoreActivityWorkerIds)
        {
            if (!byId.TryGetValue(workerId, out var worker))
            {
                continue;
            }

            items.Add(new CurrentActivityItem(
                worker.DisplayName,
                FormatActivityLabel(worker.CurrentAction),
                ClassifyActivityTone(worker.CurrentAction, isProject: false)));
        }

        foreach (var project in snapshots
                     .Where(IsProjectLifecycleWorker)
                     .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var action = FormatActivityLabel(project.CurrentAction);
            items.Add(new CurrentActivityItem(
                FormatProjectActivityLabel(project.DisplayName),
                action,
                ClassifyActivityTone(project.CurrentAction, isProject: true)));
        }

        return items;
    }

    private static bool IsProjectLifecycleWorker(WorkerHealthSnapshot snapshot) =>
        string.Equals(snapshot.Category, "Project", StringComparison.OrdinalIgnoreCase)
        && snapshot.Id.EndsWith(".state", StringComparison.OrdinalIgnoreCase);

    private static string FormatProjectActivityLabel(string displayName)
    {
        const string suffix = " — lifecycle";
        return displayName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? displayName[..^suffix.Length]
            : displayName;
    }

    private static string FormatActivityLabel(string? action) =>
        string.IsNullOrWhiteSpace(action) ? "Idle" : action;

    private static string ClassifyActivityTone(string? action, bool isProject)
    {
        var label = FormatActivityLabel(action);
        if (string.Equals(label, "Idle", StringComparison.OrdinalIgnoreCase))
        {
            return ActivityTones.Muted;
        }

        if (label.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || label.Contains("crashed", StringComparison.OrdinalIgnoreCase))
        {
            return ActivityTones.Error;
        }

        if (label.Contains("pending", StringComparison.OrdinalIgnoreCase)
            || label.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return ActivityTones.Warning;
        }

        if (label.Contains("Watching", StringComparison.OrdinalIgnoreCase)
            || label.Contains("succeeded", StringComparison.OrdinalIgnoreCase)
            || label.Contains("passed", StringComparison.OrdinalIgnoreCase)
            || (label.Contains("running", StringComparison.OrdinalIgnoreCase)
                && !label.Contains("failed", StringComparison.OrdinalIgnoreCase)))
        {
            return ActivityTones.Success;
        }

        if (label.StartsWith("Building", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Coalescing", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Publishing", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Updating tray", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Dispatcher ping", StringComparison.OrdinalIgnoreCase)
            || label.Contains("test", StringComparison.OrdinalIgnoreCase)
            || label.StartsWith("Starting", StringComparison.OrdinalIgnoreCase))
        {
            return ActivityTones.Active;
        }

        return isProject ? ActivityTones.Success : ActivityTones.Active;
    }

    private void SyncWorkerRows(IReadOnlyList<WorkerHealthSnapshot> snapshots)
    {
        var desiredIds = snapshots.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        for (var i = rows.Count - 1; i >= 0; i--)
        {
            var row = rows[i];
            if (!desiredIds.Contains(row.Id))
            {
                rows.RemoveAt(i);
                rowById.Remove(row.Id);
            }
        }

        for (var i = 0; i < snapshots.Count; i++)
        {
            var snapshot = snapshots[i];
            if (!rowById.TryGetValue(snapshot.Id, out var viewModel))
            {
                viewModel = WorkerHealthRowViewModel.FromSnapshot(snapshot);
                rowById[snapshot.Id] = viewModel;
                if (i < rows.Count)
                {
                    rows.Insert(i, viewModel);
                }
                else
                {
                    rows.Add(viewModel);
                }

                continue;
            }

            viewModel.ApplySnapshot(snapshot);

            var currentIndex = rows.IndexOf(viewModel);
            if (currentIndex < 0)
            {
                if (i < rows.Count)
                {
                    rows.Insert(i, viewModel);
                }
                else
                {
                    rows.Add(viewModel);
                }
            }
            else if (currentIndex != i)
            {
                rows.Move(currentIndex, i);
            }
        }
    }

    private void SyncCurrentActions(IReadOnlyList<CurrentActivityItem> desired)
    {
        var hasActivity = desired.Count > 0;
        if (lastHasActivity != hasActivity)
        {
            CurrentActionsIdleText.Visibility = hasActivity ? Visibility.Collapsed : Visibility.Visible;
            CurrentActionsList.Visibility = hasActivity ? Visibility.Visible : Visibility.Collapsed;
            lastHasActivity = hasActivity;
        }

        if (currentActions.Count == desired.Count)
        {
            var unchanged = true;
            for (var i = 0; i < desired.Count; i++)
            {
                if (!currentActions[i].Equals(desired[i]))
                {
                    unchanged = false;
                    break;
                }
            }

            if (unchanged)
            {
                return;
            }
        }

        currentActions.Clear();
        foreach (var item in desired)
        {
            currentActions.Add(item);
        }
    }

    private static string FormatAge(TimeSpan age) =>
        age == TimeSpan.Zero ? "never" : $"{age.TotalSeconds:F1}s ago";
}

internal static class ActivityTones
{
    public const string Muted = "muted";
    public const string Active = "active";
    public const string Success = "success";
    public const string Warning = "warning";
    public const string Error = "error";
}

internal sealed record CurrentActivityItem(string Worker, string Action, string Tone);

internal sealed class WorkerHealthRowViewModel : INotifyPropertyChanged
{
    private string id = string.Empty;
    private string statusLabel = string.Empty;
    private string currentActionLabel = "—";
    private string displayName = string.Empty;
    private string category = "—";
    private string threadLabel = "—";
    private string lastHeartbeatLocal = "—";
    private string ageLabel = "—";
    private long heartbeatCount;
    private string lastWorkLabel = "—";
    private string detail = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id
    {
        get => id;
        private set => SetField(ref id, value);
    }

    public string StatusLabel
    {
        get => statusLabel;
        private set => SetField(ref statusLabel, value);
    }

    public string CurrentActionLabel
    {
        get => currentActionLabel;
        private set => SetField(ref currentActionLabel, value);
    }

    public string DisplayName
    {
        get => displayName;
        private set => SetField(ref displayName, value);
    }

    public string Category
    {
        get => category;
        private set => SetField(ref category, value);
    }

    public string ThreadLabel
    {
        get => threadLabel;
        private set => SetField(ref threadLabel, value);
    }

    public string LastHeartbeatLocal
    {
        get => lastHeartbeatLocal;
        private set => SetField(ref lastHeartbeatLocal, value);
    }

    public string AgeLabel
    {
        get => ageLabel;
        private set => SetField(ref ageLabel, value);
    }

    public long HeartbeatCount
    {
        get => heartbeatCount;
        private set => SetField(ref heartbeatCount, value);
    }

    public string LastWorkLabel
    {
        get => lastWorkLabel;
        private set => SetField(ref lastWorkLabel, value);
    }

    public string Detail
    {
        get => detail;
        private set => SetField(ref detail, value);
    }

    public static WorkerHealthRowViewModel FromSnapshot(WorkerHealthSnapshot snapshot)
    {
        var viewModel = new WorkerHealthRowViewModel();
        viewModel.ApplySnapshot(snapshot);
        return viewModel;
    }

    public void ApplySnapshot(WorkerHealthSnapshot snapshot)
    {
        Id = snapshot.Id;
        StatusLabel = BuildStatusLabel(snapshot);
        CurrentActionLabel = string.IsNullOrWhiteSpace(snapshot.CurrentAction) ? "—" : snapshot.CurrentAction;
        DisplayName = snapshot.DisplayName;
        Category = snapshot.Category ?? "—";
        ThreadLabel = snapshot.ManagedThreadId?.ToString() ?? "—";
        LastHeartbeatLocal = snapshot.LastHeartbeatUtc == DateTimeOffset.MinValue
            ? "—"
            : snapshot.LastHeartbeatUtc.ToLocalTime().ToString("HH:mm:ss.fff");
        AgeLabel = snapshot.LastHeartbeatUtc == DateTimeOffset.MinValue
            ? "never"
            : $"{snapshot.Age.TotalSeconds:F1}s";
        HeartbeatCount = snapshot.HeartbeatCount;
        LastWorkLabel = snapshot.LastWorkDurationMs?.ToString() ?? "—";
        Detail = BuildDetail(snapshot);
    }

    private static string BuildStatusLabel(WorkerHealthSnapshot snapshot)
    {
        var status = snapshot.State switch
        {
            WorkerHealthState.Ok => "OK",
            WorkerHealthState.Stale => "Stale",
            WorkerHealthState.Unresponsive => "Blocked",
            _ => snapshot.State.ToString()
        };

        if (snapshot.TimeoutCount > 0 && snapshot.State == WorkerHealthState.Ok)
        {
            status = $"OK ({snapshot.TimeoutCount} timeouts)";
        }

        return status;
    }

    private static string BuildDetail(WorkerHealthSnapshot snapshot)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(snapshot.LastNote))
        {
            parts.Add(snapshot.LastNote);
        }

        if (snapshot.TimeoutCount > 0)
        {
            parts.Add($"timeouts={snapshot.TimeoutCount}");
        }

        return parts.Count == 0 ? "—" : string.Join("; ", parts);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
