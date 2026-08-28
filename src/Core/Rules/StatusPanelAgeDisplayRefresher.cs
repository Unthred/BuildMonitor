using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>
/// Lightweight Age column updates for the status panel BUILDS table without rebuilding hyperlinks (#93).
/// </summary>
public static class StatusPanelAgeDisplayRefresher
{
    public readonly record struct AgeDisplayCellKey(string ProjectId, string Source);

    public static IReadOnlyDictionary<AgeDisplayCellKey, string> CollectAgeDisplays(
        StatusPanelPresentation presentation)
    {
        var result = new Dictionary<AgeDisplayCellKey, string>();
        foreach (var card in presentation.Cards)
        {
            if (card.BuildSourceRows is not { Count: > 0 } rows)
            {
                continue;
            }

            foreach (var row in rows)
            {
                result[new AgeDisplayCellKey(card.ProjectId, row.Source)] = row.AgeDisplay;
            }
        }

        return result;
    }

    public static bool HasAgeDisplayChanges(
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

        var prevAges = CollectAgeDisplays(previous);
        var currAges = CollectAgeDisplays(current);
        if (prevAges.Count != currAges.Count)
        {
            return false;
        }

        foreach (var (key, value) in currAges)
        {
            if (!prevAges.TryGetValue(key, out var prior) || !string.Equals(prior, value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
