using System.Globalization;
using System.Text.Json;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.AzureDevOps;

/// <summary>
/// Shared Builds timeline JSON parser for failure navigation and active-run execution projection.
/// </summary>
public static class AzureBuildTimelineParser
{
    public static AzureBuildTimelineResult Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int? changeId = null;
        if (root.TryGetProperty("changeId", out var changeEl)
            && changeEl.ValueKind == JsonValueKind.Number
            && changeEl.TryGetInt32(out var parsedChange))
        {
            changeId = parsedChange;
        }

        if (!root.TryGetProperty("records", out var recordsEl)
            || recordsEl.ValueKind != JsonValueKind.Array)
        {
            return new AzureBuildTimelineResult(
                AzureBuildTimelineOutcome.Unavailable,
                [],
                "Malformed Azure DevOps timeline response.",
                changeId);
        }

        var records = new List<AzureBuildTimelineRecord>();
        foreach (var record in recordsEl.EnumerateArray())
        {
            if (!record.TryGetProperty("id", out var idEl)
                || !Guid.TryParse(idEl.GetString(), out var id))
            {
                continue;
            }

            Guid? parentId = null;
            if (record.TryGetProperty("parentId", out var parentEl)
                && parentEl.ValueKind == JsonValueKind.String
                && Guid.TryParse(parentEl.GetString(), out var parsedParent))
            {
                parentId = parsedParent;
            }

            var type = record.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                ? typeEl.GetString() ?? string.Empty
                : string.Empty;
            var result = record.TryGetProperty("result", out var resultEl) && resultEl.ValueKind == JsonValueKind.String
                ? resultEl.GetString()
                : null;
            var name = record.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                ? nameEl.GetString()
                : null;
            var state = record.TryGetProperty("state", out var stateEl) && stateEl.ValueKind == JsonValueKind.String
                ? stateEl.GetString()
                : null;
            int? order = null;
            if (record.TryGetProperty("order", out var orderEl)
                && orderEl.ValueKind == JsonValueKind.Number
                && orderEl.TryGetInt32(out var parsedOrder))
            {
                order = parsedOrder;
            }

            records.Add(new AzureBuildTimelineRecord(
                id,
                parentId,
                type,
                result,
                name,
                state,
                order,
                TryReadTime(record, "startTime"),
                TryReadTime(record, "finishTime")));
        }

        return new AzureBuildTimelineResult(AzureBuildTimelineOutcome.Ok, records, Message: null, changeId);
    }

    private static DateTimeOffset? TryReadTime(JsonElement record, string propertyName)
    {
        if (!record.TryGetProperty(propertyName, out var el) || el.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = el.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }
}
