using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.ControlPlane;
using BuildMonitor.Infrastructure.Services;

namespace BuildMonitor.TrayApp.Services;

/// <summary>Starts/stops the loopback control-plane HTTP host from tray settings.</summary>
public sealed class ControlPlaneHostService : IDisposable
{
    private readonly ProjectOrchestrator orchestrator;
    private readonly LocalControlPlaneHost host;
    private readonly string appDataDirectory;
    private readonly object sync = new();

    public ControlPlaneHostService(ProjectOrchestrator orchestrator, string appDataDirectory)
    {
        this.orchestrator = orchestrator;
        this.appDataDirectory = appDataDirectory;
        var coordinator = new ControlPlaneCoordinator(
            orchestrator,
            orchestrator.SessionStore,
            orchestrator.ControlPlaneEventJournal);
        host = new LocalControlPlaneHost(coordinator, orchestrator.MetricsStore);
    }

    public int? BoundPort
    {
        get
        {
            lock (sync)
            {
                return host.BoundPort;
            }
        }
    }

    public void Apply(GlobalMonitorSettings monitor)
    {
        lock (sync)
        {
            try
            {
                host.ApplySettings(monitor.ControlPlaneEnabled, monitor.ControlPlanePort);
            }
            finally
            {
                RefreshDiscoveryFile(monitor.ControlPlaneEnabled);
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            host.Dispose();
            try
            {
                ControlPlaneDiscoveryWriter.WriteDisabled(appDataDirectory);
            }
            catch
            {
                // ignore shutdown IO
            }
        }
    }

    private void RefreshDiscoveryFile(bool enabled)
    {
        try
        {
            var projects = orchestrator.ListControlPlaneProjects();
            ControlPlaneDiscoveryWriter.Write(
                appDataDirectory,
                enabled,
                host.BoundPort,
                projects);
        }
        catch
        {
            // Discovery file is best-effort for agents; do not fail tray settings apply.
        }
    }
}
