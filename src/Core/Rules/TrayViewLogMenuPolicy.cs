using BuildMonitor.Core.Settings;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Which tray context-menu projects expose local <c>View log</c>.
/// Azure-only projects are excluded — the viewer is for BuildMonitor local logs.
/// </summary>
public static class TrayViewLogMenuPolicy
{
    public const string ItemText = "View log";

    /// <summary>By-operation root label (same text as per-project action).</summary>
    public const string ByOperationRootText = "View log";

    public static bool OffersLocalViewLog(MonitoredProjectSettings project) =>
        project.IsActiveInSession && project.Local is not null;

    public static IReadOnlyList<MonitoredProjectSettings> SelectLocalLogProjects(
        IEnumerable<MonitoredProjectSettings> projects) =>
        projects.Where(OffersLocalViewLog).ToList();
}
