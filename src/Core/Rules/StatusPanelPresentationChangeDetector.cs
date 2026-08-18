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

        if (previous.HeaderStillEditingProjectId != current.HeaderStillEditingProjectId)
        {
            return true;
        }

        if (previous.SideRail.Mode != current.SideRail.Mode
            || !string.Equals(
                previous.SideRail.ActivityLabel,
                current.SideRail.ActivityLabel,
                StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var card in current.Cards)
        {
            var prev = previous.Cards.FirstOrDefault(c =>
                string.Equals(c.ProjectId, card.ProjectId, StringComparison.OrdinalIgnoreCase));
            if (prev is null)
            {
                return true;
            }

            if (prev.ShowStillEditingButton != card.ShowStillEditingButton
                || prev.ActivityState != card.ActivityState
                || !string.Equals(prev.StatusLine, card.StatusLine, StringComparison.Ordinal)
                || !string.Equals(prev.ActivityHeadline, card.ActivityHeadline, StringComparison.Ordinal)
                || !string.Equals(prev.AgentStatusLine, card.AgentStatusLine, StringComparison.Ordinal)
                || !string.Equals(prev.ControlPlaneDetailLine, card.ControlPlaneDetailLine, StringComparison.Ordinal)
                || prev.ShowControlPlaneSection != card.ShowControlPlaneSection
                || prev.ShowActivityIndicator != card.ShowActivityIndicator
                || prev.ShowProgressChart != card.ShowProgressChart
                || !ProgressStepsEqual(prev.ProgressSteps, card.ProgressSteps))
            {
                return true;
            }
        }

        return previous.Cards.Count != current.Cards.Count;
    }

    private static bool ProgressStepsEqual(
        IReadOnlyList<BuildProgressStep> left,
        IReadOnlyList<BuildProgressStep> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }
}
