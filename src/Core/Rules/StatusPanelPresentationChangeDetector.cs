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

        if (previous.SideRail != current.SideRail)
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
                || !StatusRowsEqual(prev.StatusRows, card.StatusRows)
                || !string.Equals(prev.CurrentActionText, card.CurrentActionText, StringComparison.Ordinal)
                || prev.ShowControlPlaneSection != card.ShowControlPlaneSection
                || prev.ShowActivityIndicator != card.ShowActivityIndicator
                || prev.ShowProgressChart != card.ShowProgressChart
                || !ProgressStepsEqual(prev.ProgressSteps, card.ProgressSteps)
                || prev.Azure != card.Azure
                || !BuildSourceRowsUrgentEqual(prev.BuildSourceRows, card.BuildSourceRows))
            {
                return true;
            }
        }

        return previous.Cards.Count != current.Cards.Count;
    }

    /// <summary>
    /// Urgent BUILDS rebuild when identity, status, or navigation metadata changes — not when only Age ticks.
    /// Age-only updates defer while the pointer is over the panel so Azure hyperlinks stay clickable.
    /// </summary>
    private static bool BuildSourceRowsUrgentEqual(
        IReadOnlyList<BuildSourcePresentationRow>? left,
        IReadOnlyList<BuildSourcePresentationRow>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!BuildSourceRowUrgentEqual(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool BuildSourceRowUrgentEqual(BuildSourcePresentationRow left, BuildSourcePresentationRow right) =>
        left.Source == right.Source
        && left.StatusGlyph == right.StatusGlyph
        && left.StatusText == right.StatusText
        && left.BranchDisplay == right.BranchDisplay
        && left.RunDisplay == right.RunDisplay
        && left.BuildNumberDisplay == right.BuildNumberDisplay
        && left.PullRequestDisplay == right.PullRequestDisplay
        && left.IssuesDisplay == right.IssuesDisplay
        && left.DeepLinkUrl == right.DeepLinkUrl
        && left.Emphasis == right.Emphasis
        && left.AttentionNote == right.AttentionNote;

    private static bool StatusRowsEqual(
        IReadOnlyList<StatusPanelStatusRow> left,
        IReadOnlyList<StatusPanelStatusRow> right)
    {
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
