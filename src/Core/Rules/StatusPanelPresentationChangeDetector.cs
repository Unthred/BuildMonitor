using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>Detects when derived card presentation changed enough to rebuild WPF controls.</summary>
public static class StatusPanelPresentationChangeDetector
{
    public static bool RequiresCardRebuild(
        StatusPanelPresentation? previous,
        StatusPanelPresentation current)
    {
        if (previous is null)
        {
            return true;
        }

        if (previous.Cards.Count != current.Cards.Count)
        {
            return true;
        }

        for (var i = 0; i < current.Cards.Count; i++)
        {
            if (previous.Cards[i] != current.Cards[i])
            {
                return true;
            }
        }

        return false;
    }

    public static bool RequiresUrgentCardRebuild(
        StatusPanelPresentation? previous,
        StatusPanelPresentation current)
    {
        if (previous is null)
        {
            return false;
        }

        foreach (var card in current.Cards)
        {
            var prev = previous.Cards.FirstOrDefault(c =>
                string.Equals(c.ProjectId, card.ProjectId, StringComparison.OrdinalIgnoreCase));
            if (prev is null)
            {
                continue;
            }

            if (prev.ShowStillEditingButton != card.ShowStillEditingButton)
            {
                return true;
            }
        }

        return previous.HeaderStillEditingProjectId != current.HeaderStillEditingProjectId;
    }
}
