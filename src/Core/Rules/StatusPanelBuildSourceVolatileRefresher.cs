using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Lightweight BUILDS row updates (Age + Local Status) without rebuilding hyperlinks (#93).
/// </summary>
public static class StatusPanelBuildSourceVolatileRefresher
{
    public readonly record struct BuildSourceCellKey(string ProjectId, string Source);

    public readonly record struct VolatileBuildSourceRow(
        string AgeDisplay,
        string StatusGlyph,
        string StatusText,
        StatusPanelRowEmphasis Emphasis);

    public static IReadOnlyDictionary<BuildSourceCellKey, VolatileBuildSourceRow> CollectVolatileRows(
        StatusPanelPresentation presentation)
    {
        var result = new Dictionary<BuildSourceCellKey, VolatileBuildSourceRow>();
        foreach (var card in presentation.Cards)
        {
            if (card.BuildSourceRows is not { Count: > 0 } rows)
            {
                continue;
            }

            foreach (var row in rows)
            {
                result[new BuildSourceCellKey(card.ProjectId, row.Source)] = new VolatileBuildSourceRow(
                    row.AgeDisplay,
                    row.StatusGlyph,
                    row.StatusText,
                    row.Emphasis);
            }
        }

        return result;
    }

    public static bool HasAgeOnlyChanges(
        StatusPanelPresentation? previous,
        StatusPanelPresentation current)
    {
        if (previous is null)
        {
            return false;
        }

        if (StatusPanelPresentationChangeDetector.RequiresCardRebuild(previous, current))
        {
            return false;
        }

        var prevRows = CollectVolatileRows(previous);
        var currRows = CollectVolatileRows(current);
        if (prevRows.Count != currRows.Count)
        {
            return false;
        }

        var ageChanged = false;
        foreach (var (key, curr) in currRows)
        {
            if (!prevRows.TryGetValue(key, out var prev))
            {
                return false;
            }

            if (!string.Equals(prev.AgeDisplay, curr.AgeDisplay, StringComparison.Ordinal))
            {
                ageChanged = true;
            }

            if (prev.StatusGlyph != curr.StatusGlyph
                || !string.Equals(prev.StatusText, curr.StatusText, StringComparison.Ordinal)
                || prev.Emphasis != curr.Emphasis)
            {
                return false;
            }
        }

        return ageChanged;
    }

    public static bool HasVolatilePresentationChanges(
        StatusPanelPresentation? previous,
        StatusPanelPresentation current)
    {
        if (previous is null)
        {
            return false;
        }

        if (StatusPanelPresentationChangeDetector.RequiresCardRebuild(previous, current))
        {
            return false;
        }

        var prevRows = CollectVolatileRows(previous);
        var currRows = CollectVolatileRows(current);
        if (prevRows.Count != currRows.Count)
        {
            return false;
        }

        foreach (var (key, curr) in currRows)
        {
            if (!prevRows.TryGetValue(key, out var prev))
            {
                return false;
            }

            if (prev != curr)
            {
                return true;
            }
        }

        return false;
    }
}
