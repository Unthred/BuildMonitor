using System.Globalization;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Maps typed <see cref="OperationalEvent"/> values to compact UI rows (#116).
/// Pure presentation — does not read disk or mutate health semantics.
/// </summary>
public static class OperationalHistoryPresentationMapper
{
    public const int StatusCardRowLimit = 10;
    public const int DiagnosticsRowLimit = 50;

    public static OperationalHistorySectionPresentation BuildSection(
        bool storeAvailable,
        IReadOnlyList<OperationalEvent>? eventsNewestFirst,
        int limit,
        bool expandByDefault,
        DateTimeOffset utcNow,
        TimeZoneInfo? localTimeZone = null)
    {
        if (!storeAvailable)
        {
            return new OperationalHistorySectionPresentation(
                OperationalHistoryAvailability.Unavailable,
                [],
                ExpandByDefault: false);
        }

        var tz = localTimeZone ?? TimeZoneInfo.Local;
        var source = eventsNewestFirst ?? [];
        var take = Math.Max(0, limit);
        var rows = new List<OperationalHistoryRowPresentation>(Math.Min(take, source.Count));
        foreach (var entry in source)
        {
            if (rows.Count >= take)
            {
                break;
            }

            rows.Add(MapRow(entry, utcNow, tz));
        }

        if (rows.Count == 0)
        {
            return new OperationalHistorySectionPresentation(
                OperationalHistoryAvailability.Empty,
                [],
                expandByDefault);
        }

        return new OperationalHistorySectionPresentation(
            OperationalHistoryAvailability.Available,
            rows,
            expandByDefault);
    }

    public static OperationalHistoryRowPresentation MapRow(
        OperationalEvent entry,
        DateTimeOffset utcNow,
        TimeZoneInfo? localTimeZone = null)
    {
        var tz = localTimeZone ?? TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(entry.OccurredAtUtc, tz);
        var timeLabel = FormatTimeLabel(local, utcNow, tz);
        var (sourceLabel, sourceGlyph) = FormatSource(entry.Source);
        var primary = FormatPrimaryText(entry);
        var secondary = FormatSecondaryText(entry);
        var detail = FormatDetailText(entry);
        var tip = FormatToolTip(entry, local);
        var emphasis = MapEmphasis(entry.Outcome);

        return new OperationalHistoryRowPresentation(
            entry.Id,
            entry.OccurredAtUtc,
            timeLabel,
            sourceLabel,
            sourceGlyph,
            primary,
            secondary,
            detail,
            tip,
            emphasis);
    }

    public static string FormatSourceLabel(OperationalEventSource source) =>
        FormatSource(source).Label;

    public static string FormatPrimaryText(OperationalEvent entry) =>
        entry.Kind switch
        {
            OperationalEventKind.HealthTransition => FormatHealthTransition(entry),
            OperationalEventKind.AzureRun => FormatAzureRun(entry),
            OperationalEventKind.Build => FormatBuild(entry),
            OperationalEventKind.Tests => FormatTests(entry),
            OperationalEventKind.RunHost => FormatRunHost(entry),
            OperationalEventKind.WaitingForEdits => FormatWaiting(entry),
            OperationalEventKind.ExplicitAction => FormatExplicitAction(entry),
            OperationalEventKind.WorkflowMode => FormatWorkflowMode(entry),
            _ => SanitizeSummary(entry.Summary)
        };

    public static StatusPanelRowEmphasis MapEmphasis(OperationalEventOutcome outcome) =>
        outcome switch
        {
            OperationalEventOutcome.Failed => StatusPanelRowEmphasis.Error,
            OperationalEventOutcome.Succeeded => StatusPanelRowEmphasis.Success,
            OperationalEventOutcome.Cancelled => StatusPanelRowEmphasis.Warning,
            OperationalEventOutcome.Started => StatusPanelRowEmphasis.Busy,
            OperationalEventOutcome.Changed => StatusPanelRowEmphasis.Normal,
            _ => StatusPanelRowEmphasis.Normal
        };

    public static bool SectionsEqual(
        OperationalHistorySectionPresentation? left,
        OperationalHistorySectionPresentation? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left.Availability != right.Availability
            || left.ExpandByDefault != right.ExpandByDefault
            || left.Rows.Count != right.Rows.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Rows.Count; i++)
        {
            if (!string.Equals(left.Rows[i].EventId, right.Rows[i].EventId, StringComparison.Ordinal)
                || !string.Equals(left.Rows[i].PrimaryText, right.Rows[i].PrimaryText, StringComparison.Ordinal)
                || left.Rows[i].Emphasis != right.Rows[i].Emphasis)
            {
                return false;
            }
        }

        return true;
    }

    private static (string Label, string Glyph) FormatSource(OperationalEventSource source) =>
        source switch
        {
            OperationalEventSource.Local => ("Local", "L"),
            OperationalEventSource.Azure => ("Azure", "Az"),
            OperationalEventSource.Agent => ("Agent", "Ag"),
            OperationalEventSource.User => ("User", "U"),
            OperationalEventSource.System => ("System", "S"),
            _ => ("Other", "·")
        };

    private static string FormatHealthTransition(OperationalEvent entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.PreviousValue) && !string.IsNullOrWhiteSpace(entry.NewValue))
        {
            return $"Health {entry.PreviousValue} → {entry.NewValue}";
        }

        return SanitizeSummary(entry.Summary);
    }

    private static string FormatAzureRun(OperationalEvent entry)
    {
        var build = !string.IsNullOrWhiteSpace(entry.AzureBuildNumber)
            ? $"#{entry.AzureBuildNumber}"
            : entry.AzureRunId is long id
                ? $"run {id}"
                : "run";

        return entry.Outcome switch
        {
            OperationalEventOutcome.Started => $"Run {build} started",
            OperationalEventOutcome.Succeeded => $"Run {build} succeeded",
            OperationalEventOutcome.Failed => $"Run {build} failed",
            OperationalEventOutcome.Cancelled => $"Run {build} cancelled",
            OperationalEventOutcome.Changed when !string.IsNullOrWhiteSpace(entry.PreviousValue)
                && !string.IsNullOrWhiteSpace(entry.NewValue) =>
                $"Run {build} {HumanizeEdge(entry.PreviousValue)} → {HumanizeEdge(entry.NewValue)}",
            _ => SanitizeSummary(entry.Summary)
        };
    }

    private static string FormatBuild(OperationalEvent entry) =>
        entry.Outcome switch
        {
            OperationalEventOutcome.Started => "Build started",
            OperationalEventOutcome.Succeeded => "Build succeeded",
            OperationalEventOutcome.Failed => "Build failed",
            OperationalEventOutcome.Cancelled => "Build cancelled",
            _ => SanitizeSummary(entry.Summary)
        };

    private static string FormatTests(OperationalEvent entry)
    {
        if (entry.Outcome == OperationalEventOutcome.Failed)
        {
            var count = entry.Detail?.TestFailedCount;
            return count is > 0
                ? $"Tests failed · {count} failing"
                : "Tests failed";
        }

        return entry.Outcome switch
        {
            OperationalEventOutcome.Started => "Tests started",
            OperationalEventOutcome.Succeeded => "Tests succeeded",
            OperationalEventOutcome.Cancelled => "Tests cancelled",
            _ => SanitizeSummary(entry.Summary)
        };
    }

    private static string FormatRunHost(OperationalEvent entry) =>
        entry.Detail?.ActionName switch
        {
            "host-restarted" => "Host restarted",
            "host-started" => "Host started",
            "host-stopped" => "Host stopped",
            "host-crashed" => "Host crashed",
            _ => SanitizeSummary(entry.Summary)
        };

    private static string FormatWaiting(OperationalEvent entry) =>
        entry.Outcome == OperationalEventOutcome.Started
            ? "Waiting for edits"
            : "Resumed from edit wait";

    private static string FormatExplicitAction(OperationalEvent entry)
    {
        var name = entry.Detail?.ActionName;
        return name switch
        {
            "rebuild" => "Rebuild requested",
            "tests" => "Tests requested",
            "ship-check" => "Ship-check requested",
            "run-start" => "Run start requested",
            "run-restart" => "Run restart requested",
            "run-stop" => "Run stop requested",
            "file-triggered-build" => "File-triggered build",
            _ => SanitizeSummary(entry.Summary)
        };
    }

    private static string FormatWorkflowMode(OperationalEvent entry) =>
        !string.IsNullOrWhiteSpace(entry.NewValue)
            ? $"Mode → {HumanizeMode(entry.NewValue)}"
            : SanitizeSummary(entry.Summary);

    private static string? FormatSecondaryText(OperationalEvent entry)
    {
        if (entry.Kind == OperationalEventKind.AzureRun && !string.IsNullOrWhiteSpace(entry.Branch))
        {
            return entry.Branch;
        }

        if (entry.Kind == OperationalEventKind.Tests
            && entry.Outcome == OperationalEventOutcome.Failed
            && entry.Detail?.FailingTestNames is { Count: > 0 } names)
        {
            return string.Join(", ", names.Take(3));
        }

        if (entry.Kind == OperationalEventKind.Build && entry.LocalBuildNumber is int n)
        {
            return $"#{n}";
        }

        return null;
    }

    private static string? FormatDetailText(OperationalEvent entry)
    {
        if (entry.Detail is null)
        {
            return null;
        }

        var parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(entry.Detail.ErrorPreview))
        {
            parts.Add(TrimOneLine(entry.Detail.ErrorPreview!, 120));
        }

        if (entry.Detail.FailingTestNames is { Count: > 0 })
        {
            parts.Add(string.Join(", ", entry.Detail.FailingTestNames.Take(OperationalEventDetail.MaxFailingTestNames)));
        }

        if (entry.AzureRunId is long runId)
        {
            parts.Add($"Azure run {runId}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Detail.HoldReason))
        {
            parts.Add(entry.Detail.HoldReason!);
        }

        if (entry.Detail.LogKind is BuildLogKind logKind)
        {
            parts.Add($"Log: {logKind}");
        }

        if (entry.Detail.ExitCode is int code and not 0)
        {
            parts.Add($"Exit {code}");
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string FormatToolTip(OperationalEvent entry, DateTimeOffset local)
    {
        var tip = $"{local:yyyy-MM-dd HH:mm:ss} · {FormatSource(entry.Source).Label} · {FormatPrimaryText(entry)}";
        var detail = FormatDetailText(entry);
        return string.IsNullOrWhiteSpace(detail) ? tip : $"{tip}\n{detail}";
    }

    private static string FormatTimeLabel(
        DateTimeOffset local,
        DateTimeOffset utcNow,
        TimeZoneInfo tz)
    {
        var nowLocal = TimeZoneInfo.ConvertTime(utcNow, tz);
        if (local.Date == nowLocal.Date)
        {
            return local.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        if (local.Year == nowLocal.Year)
        {
            return local.ToString("dd MMM HH:mm", CultureInfo.InvariantCulture);
        }

        return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private static string HumanizeEdge(string edge)
    {
        // Edges are "State/Result" from Azure emitters — keep readable, not enum dumps.
        return edge.Replace("Unknown", "—", StringComparison.Ordinal);
    }

    private static string HumanizeMode(string mode) =>
        mode.Replace('-', ' ');

    private static string SanitizeSummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return "Activity";
        }

        var text = summary.Trim();
        text = text.Replace("OperationalEventKind.", "", StringComparison.Ordinal);
        text = text.Replace("OperationalEventOutcome.", "", StringComparison.Ordinal);
        text = text.Replace("Outcome=", "", StringComparison.Ordinal);
        return text;
    }

    private static string TrimOneLine(string text, int max)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (flat.Length <= max)
        {
            return flat;
        }

        return flat[..(max - 1)] + "…";
    }
}
