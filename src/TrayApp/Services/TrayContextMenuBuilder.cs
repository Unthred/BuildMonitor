using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Services;
using Forms = System.Windows.Forms;

namespace BuildMonitor.TrayApp.Services;

/// <summary>
/// Builds the tray icon context menu from settings and project orchestrator state.
/// </summary>
public sealed class TrayContextMenuBuilder
{
    public sealed class Host
    {
        public required Action<Action> RunUi { get; init; }
        public required Action<Func<Task>> RunBackground { get; init; }
        public required Action ShowStatus { get; init; }
        public required Action ShowBuildDiagnostics { get; init; }
        public required Action ShowBuildMonitorHealth { get; init; }
        public required Action ShowSettings { get; init; }
        public required Action RequestExit { get; init; }
        public required Action<string, string?> OpenLogViewerForProject { get; init; }
        public required Action<IReadOnlyList<MonitoredProjectSettings>> StartRunTestsForProjects { get; init; }
        public required Action<string, string> InstallControlPlaneAgentSkill { get; init; }
    }

    public void Rebuild(
        Forms.ContextMenuStrip menu,
        AppSettings settings,
        ProjectOrchestrator orchestrator,
        Host host)
    {
        var active = settings.Projects.Where(p => p.IsActiveInSession && p.Local is not null).ToList();

        menu.Items.Clear();

        menu.Items.Add(new Forms.ToolStripMenuItem(
            "Status",
            null,
            (_, _) => host.RunUi(host.ShowStatus)));
        menu.Items.Add(new Forms.ToolStripSeparator());

        if (settings.AppBehavior.TrayMenuLayout == TrayMenuLayout.ByProject)
        {
            AddByProjectItems(menu.Items, active, orchestrator, host);
        }
        else
        {
            menu.Items.Add(BuildRebuildMenu(active, orchestrator, host));
            menu.Items.Add(BuildRestartMenu(active, orchestrator, host));
            menu.Items.Add(BuildRunTestsMenu(active, host));
            menu.Items.Add(BuildStopMenu(active, orchestrator, host));
            menu.Items.Add(BuildViewLogsMenu(active, host));
            menu.Items.Add(BuildCleanOutputMenu(active, orchestrator, host));
            menu.Items.Add(BuildInstallAgentSkillMenu(active, host));
        }

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem(
            "Build diagnostics…",
            null,
            (_, _) => host.RunUi(host.ShowBuildDiagnostics)));
        menu.Items.Add(new Forms.ToolStripMenuItem(
            "Build Monitor Health…",
            null,
            (_, _) => host.RunUi(host.ShowBuildMonitorHealth)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem(
            "Settings",
            null,
            (_, _) => host.RunUi(host.ShowSettings)));
        menu.Items.Add(new Forms.ToolStripMenuItem(
            "Exit",
            null,
            (_, _) => host.RequestExit()));
    }

    private static void AddByProjectItems(
        Forms.ToolStripItemCollection items,
        List<MonitoredProjectSettings> active,
        ProjectOrchestrator orchestrator,
        Host host)
    {
        if (active.Count == 0)
        {
            items.Add(new Forms.ToolStripMenuItem("(No active projects)") { Enabled = false });
            return;
        }

        foreach (var project in active)
        {
            var id = project.Id;
            var restartable = project.Local!.RunOptions.RunMode != ProjectRunMode.None;
            var submenu = new Forms.ToolStripMenuItem(project.DisplayName);

            submenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Rebuild", null, (_, _) =>
                host.RunBackground(() => orchestrator.RebuildAsync(id, CancellationToken.None))));

            if (restartable)
            {
                submenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Restart app", null, (_, _) =>
                    host.RunBackground(() => orchestrator.RestartAppAsync(id, CancellationToken.None))));
                submenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Rebuild & restart", null, (_, _) =>
                    host.RunBackground(() => orchestrator.RebuildAndRestartAsync(id, CancellationToken.None))));
            }

            submenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Run tests", null, (_, _) =>
                host.RunBackground(() => orchestrator.RunTestsAsync(id, CancellationToken.None))));
            submenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Stop", null, (_, _) =>
                host.RunBackground(() => orchestrator.StopProjectAsync(id))));
            submenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            submenu.DropDownItems.Add(new Forms.ToolStripMenuItem("View log", null, (_, _) =>
                host.RunUi(() => host.OpenLogViewerForProject(id, null))));
            submenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Clean build output", null, (_, _) =>
                host.RunBackground(() => orchestrator.RepairBuildOutputAsync(id, CancellationToken.None))));
            submenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            var root = project.Local!.RootFolder;
            var name = project.DisplayName;
            submenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Install Cursor agent skill…", null, (_, _) =>
                host.RunUi(() => host.InstallControlPlaneAgentSkill(root, name))));

            items.Add(submenu);
        }
    }

    private static Forms.ToolStripMenuItem BuildRebuildMenu(
        List<MonitoredProjectSettings> active,
        ProjectOrchestrator orchestrator,
        Host host)
    {
        var menu = new Forms.ToolStripMenuItem("Rebuild");
        menu.DropDownItems.Add(new Forms.ToolStripMenuItem("All Active", null, (_, _) =>
            host.RunBackground(async () =>
            {
                foreach (var p in active)
                {
                    await orchestrator.RebuildAsync(p.Id, CancellationToken.None);
                }
            })));

        if (active.Count > 0)
        {
            menu.DropDownItems.Add(new Forms.ToolStripSeparator());
            foreach (var project in active)
            {
                var id = project.Id;
                menu.DropDownItems.Add(new Forms.ToolStripMenuItem(project.DisplayName, null, (_, _) =>
                    host.RunBackground(() => orchestrator.RebuildAsync(id, CancellationToken.None))));
            }
        }

        return menu;
    }

    private static Forms.ToolStripMenuItem BuildRestartMenu(
        List<MonitoredProjectSettings> active,
        ProjectOrchestrator orchestrator,
        Host host)
    {
        var menu = new Forms.ToolStripMenuItem("Restart app");
        var restartable = active.Where(p => p.Local!.RunOptions.RunMode != ProjectRunMode.None).ToList();
        menu.Enabled = restartable.Count > 0;

        if (restartable.Count == 0)
        {
            return menu;
        }

        menu.DropDownItems.Add(new Forms.ToolStripMenuItem("Restart all active", null, (_, _) =>
            host.RunBackground(async () =>
            {
                foreach (var p in restartable)
                {
                    await orchestrator.RestartAppAsync(p.Id, CancellationToken.None);
                }
            })));

        menu.DropDownItems.Add(new Forms.ToolStripMenuItem("Rebuild & restart all active", null, (_, _) =>
            host.RunBackground(async () =>
            {
                foreach (var p in restartable)
                {
                    await orchestrator.RebuildAndRestartAsync(p.Id, CancellationToken.None);
                }
            })));

        menu.DropDownItems.Add(new Forms.ToolStripSeparator());
        foreach (var project in restartable)
        {
            var id = project.Id;
            var name = project.DisplayName;
            menu.DropDownItems.Add(new Forms.ToolStripMenuItem($"Restart — {name}", null, (_, _) =>
                host.RunBackground(() => orchestrator.RestartAppAsync(id, CancellationToken.None))));
            menu.DropDownItems.Add(new Forms.ToolStripMenuItem($"Rebuild & restart — {name}", null, (_, _) =>
                host.RunBackground(() => orchestrator.RebuildAndRestartAsync(id, CancellationToken.None))));
        }

        return menu;
    }

    private static Forms.ToolStripMenuItem BuildRunTestsMenu(List<MonitoredProjectSettings> active, Host host)
    {
        var menu = new Forms.ToolStripMenuItem("Run tests") { Enabled = active.Count > 0 };
        if (active.Count == 0)
        {
            return menu;
        }

        menu.DropDownItems.Add(new Forms.ToolStripMenuItem("All Active", null, (_, _) =>
            host.RunUi(() => host.StartRunTestsForProjects(active))));

        menu.DropDownItems.Add(new Forms.ToolStripSeparator());
        foreach (var project in active)
        {
            menu.DropDownItems.Add(new Forms.ToolStripMenuItem(project.DisplayName, null, (_, _) =>
                host.RunUi(() => host.StartRunTestsForProjects([project]))));
        }

        return menu;
    }

    private static Forms.ToolStripMenuItem BuildStopMenu(
        List<MonitoredProjectSettings> active,
        ProjectOrchestrator orchestrator,
        Host host)
    {
        var menu = new Forms.ToolStripMenuItem("Stop");
        menu.DropDownItems.Add(new Forms.ToolStripMenuItem("All Active", null, (_, _) =>
            host.RunBackground(() => orchestrator.StopAllAsync())));

        if (active.Count > 0)
        {
            menu.DropDownItems.Add(new Forms.ToolStripSeparator());
            foreach (var project in active)
            {
                var id = project.Id;
                menu.DropDownItems.Add(new Forms.ToolStripMenuItem(project.DisplayName, null, (_, _) =>
                    host.RunBackground(() => orchestrator.StopProjectAsync(id))));
            }
        }

        return menu;
    }

    private static Forms.ToolStripMenuItem BuildViewLogsMenu(List<MonitoredProjectSettings> active, Host host)
    {
        var menu = new Forms.ToolStripMenuItem("View Log") { Enabled = active.Count > 0 };
        foreach (var project in active)
        {
            var id = project.Id;
            var name = project.DisplayName;
            menu.DropDownItems.Add(new Forms.ToolStripMenuItem(name, null, (_, _) =>
                host.RunUi(() => host.OpenLogViewerForProject(id, name))));
        }

        return menu;
    }

    private static Forms.ToolStripMenuItem BuildCleanOutputMenu(
        List<MonitoredProjectSettings> active,
        ProjectOrchestrator orchestrator,
        Host host)
    {
        var menu = new Forms.ToolStripMenuItem("Clean build output") { Enabled = active.Count > 0 };
        if (active.Count == 0)
        {
            return menu;
        }

        menu.DropDownItems.Add(new Forms.ToolStripMenuItem("All active", null, (_, _) =>
            host.RunBackground(async () =>
            {
                foreach (var project in active)
                {
                    await orchestrator.RepairBuildOutputAsync(project.Id, CancellationToken.None);
                }
            })));

        menu.DropDownItems.Add(new Forms.ToolStripSeparator());
        foreach (var project in active)
        {
            var id = project.Id;
            menu.DropDownItems.Add(new Forms.ToolStripMenuItem(project.DisplayName, null, (_, _) =>
                host.RunBackground(() => orchestrator.RepairBuildOutputAsync(id, CancellationToken.None))));
        }

        return menu;
    }

    private static Forms.ToolStripMenuItem BuildInstallAgentSkillMenu(
        List<MonitoredProjectSettings> active,
        Host host)
    {
        var menu = new Forms.ToolStripMenuItem("Install Cursor agent skill") { Enabled = active.Count > 0 };
        foreach (var project in active)
        {
            var root = project.Local!.RootFolder;
            var name = project.DisplayName;
            menu.DropDownItems.Add(new Forms.ToolStripMenuItem(name, null, (_, _) =>
                host.RunUi(() => host.InstallControlPlaneAgentSkill(root, name))));
        }

        return menu;
    }
}
