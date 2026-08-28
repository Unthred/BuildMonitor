using BuildMonitor.Core.Models;

namespace BuildMonitor.Core.Rules;

/// <summary>Whether Local BUILDS Status should open the project log viewer on click.</summary>
public enum LocalBuildStatusLogAction
{
    None = 0,
    OpenLogWithWarningsFilter = 1,
    OpenLogWithErrorsFilter = 2
}

public static class StatusPanelLocalStatusActionRules
{
    public static LocalBuildStatusLogAction Resolve(BuildSourcePresentationRow row)
    {
        if (!string.Equals(row.Source, "Local", StringComparison.OrdinalIgnoreCase))
        {
            return LocalBuildStatusLogAction.None;
        }

        return row.Emphasis switch
        {
            StatusPanelRowEmphasis.Error => LocalBuildStatusLogAction.OpenLogWithErrorsFilter,
            StatusPanelRowEmphasis.Warning => LocalBuildStatusLogAction.OpenLogWithWarningsFilter,
            _ => LocalBuildStatusLogAction.None
        };
    }
}
