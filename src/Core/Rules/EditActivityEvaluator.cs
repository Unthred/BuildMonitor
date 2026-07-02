namespace BuildMonitor.Core.Rules;

public sealed record EditActivityInput(
    bool SourceWatcherHasPendingChanges,
    DateTimeOffset? SourceBurstStartedUtc,
    DateTimeOffset LastMeaningfulFileChangeUtc,
    DateTimeOffset? LastAgentActivityUtc,
    int DebounceMs,
    bool UseAgentTranscriptActivity);

public sealed record EditActivitySnapshot(
    bool IsActive,
    DateTimeOffset QuietUntilUtc,
    string PrimaryReason)
{
    public static EditActivitySnapshot Inactive { get; } = new(false, DateTimeOffset.UtcNow, string.Empty);

    public static EditActivitySnapshot Evaluate(EditActivityInput input, DateTimeOffset utcNow)
    {
        var debounceMs = Math.Clamp(input.DebounceMs, 1500, 12000);
        var reasons = new List<string>();

        if (input.SourceWatcherHasPendingChanges)
        {
            reasons.Add("source saves in debounce window");
        }

        if (input.LastMeaningfulFileChangeUtc != DateTimeOffset.MinValue)
        {
            var quietUntil = ComputeQuietUntil(input.LastMeaningfulFileChangeUtc, debounceMs);
            if (quietUntil > utcNow)
            {
                reasons.Add("quiet period after last save");
                return new EditActivitySnapshot(true, quietUntil, string.Join("; ", reasons));
            }
        }

        if (input.UseAgentTranscriptActivity
            && input.LastAgentActivityUtc is { } agentActivity)
        {
            var agentQuietUntil = ComputeQuietUntil(agentActivity, debounceMs);
            if (agentQuietUntil > utcNow)
            {
                reasons.Add("agent tooling activity");
                return new EditActivitySnapshot(true, agentQuietUntil, string.Join("; ", reasons));
            }
        }

        if (input.SourceWatcherHasPendingChanges)
        {
            var burstStart = input.SourceBurstStartedUtc ?? utcNow;
            var burstQuietUntil = ComputeQuietUntil(input.LastMeaningfulFileChangeUtc != DateTimeOffset.MinValue
                ? input.LastMeaningfulFileChangeUtc
                : burstStart, debounceMs);
            if (burstQuietUntil > utcNow)
            {
                return new EditActivitySnapshot(true, burstQuietUntil, string.Join("; ", reasons));
            }
        }

        return Inactive;
    }

    private static DateTimeOffset ComputeQuietUntil(DateTimeOffset lastChangeUtc, int debounceMs) =>
        lastChangeUtc.AddMilliseconds(debounceMs);
}
