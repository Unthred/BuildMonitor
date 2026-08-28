using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.Navigation;

/// <summary>In-memory snapshot of discovered registered browsers.</summary>
public sealed class RegisteredBrowserCatalog : IRegisteredBrowserCatalog
{
    private readonly object sync = new();
    private IReadOnlyList<RegisteredBrowserDescriptor> browsers = [];

    public RegisteredBrowserCatalog(bool discoverOnConstruction = true)
    {
        if (discoverOnConstruction)
        {
            Refresh();
        }
    }

    public IReadOnlyList<RegisteredBrowserDescriptor> GetBrowsers()
    {
        lock (sync)
        {
            return browsers;
        }
    }

    public void Refresh()
    {
        var discovered = WindowsRegisteredBrowserDiscovery.Discover();
        lock (sync)
        {
            browsers = discovered;
        }
    }

    public bool TryResolve(string? registeredBrowserId, out RegisteredBrowserDescriptor? browser)
    {
        browser = null;
        if (string.IsNullOrWhiteSpace(registeredBrowserId))
        {
            return false;
        }

        lock (sync)
        {
            browser = browsers.FirstOrDefault(
                b => string.Equals(b.RegisteredBrowserId, registeredBrowserId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return browser is not null;
    }
}
