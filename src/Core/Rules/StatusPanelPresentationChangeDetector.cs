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
            if (!CardPresentationRebuildEqual(previous.Cards[i], current.Cards[i]))
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
                || !BuildSourceRowsUrgentEqual(prev.BuildSourceRows, card.BuildSourceRows)
                || !HiddenAzureSectionUrgentEqual(prev, card))
            {
                return true;
            }
        }

        return previous.Cards.Count != current.Cards.Count;
    }

    /// <summary>
    /// Full card rebuild equality — ignores BUILDS AgeDisplay ticks and hidden Azure timing
    /// when the BUILDS table is the active Azure surface.
    /// </summary>
    private static bool CardPresentationRebuildEqual(
        StatusPanelCardPresentation left,
        StatusPanelCardPresentation right) =>
        left.ProjectId == right.ProjectId
        && left.Health == right.Health
        && left.DisplayName == right.DisplayName
        && left.ShowSiteReady == right.ShowSiteReady
        && left.ShowSiteAwaiting == right.ShowSiteAwaiting
        && left.ListenUrl == right.ListenUrl
        && left.ShowProgressChart == right.ShowProgressChart
        && ProgressStepsEqual(left.ProgressSteps, right.ProgressSteps)
        && left.ShowErrorPreview == right.ShowErrorPreview
        && left.ErrorPreview == right.ErrorPreview
        && left.ShowActivityIndicator == right.ShowActivityIndicator
        && left.ActivityState == right.ActivityState
        && left.ErrorCount == right.ErrorCount
        && left.WarningCount == right.WarningCount
        && left.ShowCopyErrorsButton == right.ShowCopyErrorsButton
        && left.ShowRestartButtons == right.ShowRestartButtons
        && left.ShowRunTestsButton == right.ShowRunTestsButton
        && left.ShowStillEditingButton == right.ShowStillEditingButton
        && left.StillEditingToolTip == right.StillEditingToolTip
        && left.ShowControlPlaneSection == right.ShowControlPlaneSection
        && StatusRowsEqual(left.StatusRows, right.StatusRows)
        && string.Equals(left.CurrentActionText, right.CurrentActionText, StringComparison.Ordinal)
        && BuildSourceRowsUrgentEqual(left.BuildSourceRows, right.BuildSourceRows)
        && HiddenAzureSectionRebuildEqual(left, right);

    /// <summary>
    /// When BUILDS rows are shown, the legacy Azure block is hidden — its timing ticks must not rebuild hyperlinks.
    /// </summary>
    private static bool HiddenAzureSectionUrgentEqual(
        StatusPanelCardPresentation left,
        StatusPanelCardPresentation right)
    {
        if (right.BuildSourceRows is { Count: > 0 })
        {
            return true;
        }

        return left.Azure == right.Azure;
    }

    private static bool HiddenAzureSectionRebuildEqual(
        StatusPanelCardPresentation left,
        StatusPanelCardPresentation right) =>
        HiddenAzureSectionUrgentEqual(left, right);

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
        && AzureBuildSourceNavigationSemanticEqual.Equals(left.AzureNavigation, right.AzureNavigation)
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
