using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

public static class TrayTooltipFormatter
{
    public const int MaxTooltipLength = 63;

    public static string Format(
        ProjectHealthSnapshot? headline,
        MonitorHealth health,
        bool isBuilding)
    {
        if (isBuilding)
        {
            var name = headline?.DisplayName ?? "project";
            return Truncate($"Building — {name}");
        }

        if (headline is null)
        {
            return DescribeHealthTooltip(health);
        }

        if (headline.Health == MonitorHealth.Red)
        {
            var phase = string.IsNullOrWhiteSpace(headline.FailurePhase)
                ? "Failed"
                : headline.FailurePhase;
            if (!string.IsNullOrWhiteSpace(headline.LastErrorPreview))
            {
                return Truncate($"{headline.DisplayName} — {phase}: {headline.LastErrorPreview}");
            }

            return Truncate($"{headline.DisplayName} — {phase}");
        }

        if (headline.Health == MonitorHealth.Amber)
        {
            return Truncate($"{headline.DisplayName} — Warnings");
        }

        if (headline.ListenUrlReady && !string.IsNullOrWhiteSpace(headline.ListenUrl))
        {
            return Truncate($"{headline.DisplayName} — Site up · {headline.ListenUrl}");
        }

        return Truncate($"{headline.DisplayName} — OK");
    }

    public static string Truncate(string text, int maxLength = MaxTooltipLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";

    public static string DescribeHealth(MonitorHealth health) =>
        health switch
        {
            MonitorHealth.Green => "OK",
            MonitorHealth.Amber => "Warnings",
            MonitorHealth.Red => "Errors",
            _ => "Unknown"
        };

    public static string DescribeHealthTooltip(MonitorHealth health) =>
        health switch
        {
            MonitorHealth.Green => "Build monitor - Success",
            MonitorHealth.Amber => "Build monitor - Warnings",
            MonitorHealth.Red => "Build monitor - Failed",
            _ => "Build Monitor"
        };
}
