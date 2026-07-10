using BuildMonitor.Core.Rules;

namespace BuildMonitor.Infrastructure.Diagnostics;

public static class InFlightBuildUnexpectedNoteFormatter
{
    public static string Format(EditActivitySnapshot activity, DateTimeOffset utcNow)
    {
        var parts = new List<string> { "Status panel — AI still working during this build." };

        if (activity.IsActive)
        {
            if (!string.IsNullOrWhiteSpace(activity.PrimaryReason))
            {
                parts.Add(activity.PrimaryReason);
            }

            var remaining = activity.QuietUntilUtc - utcNow;
            if (remaining > TimeSpan.Zero)
            {
                parts.Add($"quiet ~{Math.Ceiling(remaining.TotalSeconds)} s remaining");
            }
        }
        else
        {
            parts.Add("no active edit-gating signal at click time");
        }

        return string.Join(" · ", parts);
    }
}
