using BuildMonitor.Core.Models;



namespace BuildMonitor.Core.Rules;



public static class TrayTooltipFormatter

{

    /// <summary>Legacy shell tooltip limit — native tooltip is suppressed; full text is in the custom hover hint.</summary>

    public const int MaxTooltipLength = 63;



    public static string Format(

        ProjectHealthSnapshot? headline,

        MonitorHealth health,

        bool isBuilding) =>

        FormatMultiline(headline, health, isBuilding);



    public static string FormatMultiline(

        ProjectHealthSnapshot? headline,

        MonitorHealth health,

        bool isBuilding)

    {

        if (isBuilding)

        {

            var lines = new List<string>();

            if (headline is not null)

            {

                lines.Add($"Building — {headline.DisplayName}");

                AppendIssueCountLine(lines, headline.ErrorCount, headline.WarningCount, headline.IssueCountsText);

            }

            else

            {

                lines.Add("Building…");

            }



            return JoinLines(lines);

        }



        if (headline is null)

        {

            return DescribeHealthTooltip(health);

        }



        var result = new List<string>

        {

            $"{headline.DisplayName} — {BuildStatusLine(headline)}"

        };

        AppendIssueCountLine(result, headline.ErrorCount, headline.WarningCount, headline.IssueCountsText);



        if (!string.IsNullOrWhiteSpace(headline.LastErrorPreview))

        {

            result.Add(headline.LastErrorPreview.Trim());

        }

        else if (headline.ListenUrlReady && !string.IsNullOrWhiteSpace(headline.ListenUrl))

        {

            result.Add(headline.ListenUrl.Trim());

        }



        return JoinLines(result);

    }



    public static string FormatMultiline(

        IReadOnlyList<ProjectHealthSnapshot> activeProjects,

        MonitorHealth rollupHealth,

        bool isBuilding)

    {

        var active = activeProjects.Where(p => p.IsActive).ToList();

        if (active.Count == 0)

        {

            return DescribeHealthTooltip(rollupHealth);

        }



        if (active.Count == 1)

        {

            return FormatMultiline(active[0], rollupHealth, isBuilding);

        }



        var blocks = active

            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)

            .Select(p => FormatMultiline(

                p,

                p.Health,

                p.State is ProjectLifecycleState.Building or ProjectLifecycleState.Testing))

            .Where(block => !string.IsNullOrWhiteSpace(block));



        return JoinBlocks(blocks);

    }



    public static string FormatShort(

        ProjectHealthSnapshot? headline,

        MonitorHealth health,

        bool isBuilding) =>

        string.Empty;



    public static string FormatCompactIssueCounts(int errorCount, int warningCount)

    {

        if (errorCount <= 0 && warningCount <= 0)

        {

            return string.Empty;

        }



        if (errorCount > 0 && warningCount > 0)

        {

            return $" · {errorCount}e/{warningCount}w";

        }



        return errorCount > 0

            ? $" · {errorCount} err"

            : $" · {warningCount} warn";

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



    private static string BuildStatusLine(ProjectHealthSnapshot headline)

    {

        if (headline.Health == MonitorHealth.Red)

        {

            return string.IsNullOrWhiteSpace(headline.FailurePhase)

                ? "Failed"

                : headline.FailurePhase;

        }



        if (headline.Health == MonitorHealth.Amber)

        {

            return "Warnings";

        }



        if (headline.ListenUrlReady && !string.IsNullOrWhiteSpace(headline.ListenUrl))

        {

            return "Site up";

        }



        return "OK";

    }



    private static void AppendIssueCountLine(

        List<string> lines,

        int errorCount,

        int warningCount,

        string? issueCountsText)

    {

        if (!string.IsNullOrWhiteSpace(issueCountsText))

        {

            lines.Add(issueCountsText);

            return;

        }



        if (errorCount <= 0 && warningCount <= 0)

        {

            return;

        }



        lines.Add(errorCount > 0 && warningCount > 0

            ? $"{errorCount} errors · {warningCount} warnings"

            : errorCount > 0

                ? $"{errorCount} errors"

                : $"{warningCount} warnings");

    }



    private static string JoinLines(IEnumerable<string> lines) =>

        string.Join(

            Environment.NewLine,

            lines.Where(line => !string.IsNullOrWhiteSpace(line)));



    private static string JoinBlocks(IEnumerable<string> blocks) =>

        string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks);

}


