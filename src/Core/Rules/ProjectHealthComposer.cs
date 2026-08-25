using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>Merges local project health with an optional Azure facet into overall tray health.</summary>
public static class ProjectHealthComposer
{
    public static MonitorHealth Merge(MonitorHealth localHealth, ProjectAzureHealthFacet? azure)
    {
        var azureContribution = azure is null
            ? null
            : AzureHealthContribution.ToTrayContribution(azure.CiState, azure.Availability);

        if (azureContribution is null)
        {
            return localHealth;
        }

        return Worst(localHealth, azureContribution.Value);
    }

    public static string ToLabel(MonitorHealth health) => ProjectHealthEvaluator.ToLabel(health);

    public static MonitorHealth Worst(MonitorHealth a, MonitorHealth b)
    {
        if (a == MonitorHealth.Red || b == MonitorHealth.Red)
        {
            return MonitorHealth.Red;
        }

        if (a == MonitorHealth.Amber || b == MonitorHealth.Amber)
        {
            return MonitorHealth.Amber;
        }

        if (a == MonitorHealth.Green || b == MonitorHealth.Green)
        {
            // Prefer Green when either side is Green and neither is worse;
            // Unknown + Green → Green; Unknown + Unknown → Unknown
            if (a == MonitorHealth.Green && b == MonitorHealth.Green)
            {
                return MonitorHealth.Green;
            }

            if (a == MonitorHealth.Green && b == MonitorHealth.Unknown)
            {
                return MonitorHealth.Green;
            }

            if (b == MonitorHealth.Green && a == MonitorHealth.Unknown)
            {
                return MonitorHealth.Green;
            }

            return MonitorHealth.Green;
        }

        return MonitorHealth.Unknown;
    }

    public static ProjectHealthSnapshot WithAzure(ProjectHealthSnapshot snapshot, ProjectAzureHealthFacet? azure)
    {
        var merged = Merge(snapshot.Health, azure);
        return snapshot with
        {
            Azure = azure,
            Health = merged,
            HealthLabel = ToLabel(merged)
        };
    }
}
