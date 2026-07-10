namespace BuildMonitor.Core.Rules;

/// <summary>
/// Resolves the effective rebuild quiet-until instant shown in the UI and used by the wait loop.
/// Mirrors <c>WaitForEditQuietThenBuildAsync</c>: max(file-change quiet, agent/edit activity quiet).
/// </summary>
public static class EditGatingQuietUntilResolver
{
    public static DateTimeOffset? Resolve(
        bool pendingFileChangeRebuild,
        DateTimeOffset lastMeaningfulFileChangeUtc,
        int debounceMs,
        EditActivitySnapshot activity)
    {
        DateTimeOffset? quietUntil = null;

        if (pendingFileChangeRebuild && lastMeaningfulFileChangeUtc != DateTimeOffset.MinValue)
        {
            quietUntil = ComputeQuietUntil(lastMeaningfulFileChangeUtc, debounceMs);
        }
        else if (activity.IsActive)
        {
            quietUntil = activity.QuietUntilUtc;
        }

        if (activity.IsActive && activity.QuietUntilUtc > (quietUntil ?? DateTimeOffset.MinValue))
        {
            quietUntil = activity.QuietUntilUtc;
        }

        return quietUntil;
    }

    private static DateTimeOffset ComputeQuietUntil(DateTimeOffset lastChangeUtc, int debounceMs) =>
        lastChangeUtc.AddMilliseconds(Math.Clamp(debounceMs, 1500, 12000));
}
