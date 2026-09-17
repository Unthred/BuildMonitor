using System.Text.RegularExpressions;

namespace BuildMonitor.Core.Rules;

public enum PublishingIntent
{
    OrdinaryWork = 0,
    OpenPullRequest = 1,
    Merge = 2
}

/// <summary>
/// Current-instruction publishing states. "ship" alone is not merge authorization;
/// green CI is not human merge approval.
/// </summary>
public static class PublishingAuthorization
{
    public static bool GreenCiIsHumanMergeApproval => false;

    public static PublishingIntent Resolve(string? currentInstruction)
    {
        if (string.IsNullOrWhiteSpace(currentInstruction))
        {
            return PublishingIntent.OrdinaryWork;
        }

        var text = currentInstruction.Trim();
        if (IsMergeAuthorized(text))
        {
            return PublishingIntent.Merge;
        }

        if (ImpliesOpenPullRequest(text))
        {
            return PublishingIntent.OpenPullRequest;
        }

        return PublishingIntent.OrdinaryWork;
    }

    public static bool AllowsMerge(PublishingIntent intent) => intent == PublishingIntent.Merge;

    public static bool AllowsOpenPullRequest(PublishingIntent intent) =>
        intent is PublishingIntent.OpenPullRequest or PublishingIntent.Merge;

    public static bool IsMergeAuthorized(string currentInstruction) =>
        ContainsCompleteTheShip(currentInstruction) || ContainsMergeWord(currentInstruction);

    private static bool ImpliesOpenPullRequest(string text)
    {
        if (ContainsOpenPullRequestPhrase(text))
        {
            return true;
        }

        // "ship" / "ship it" may imply preparing a PR, but never merging.
        return ContainsShipWord(text);
    }

    private static bool ContainsCompleteTheShip(string text) =>
        text.Contains("complete the ship", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsMergeWord(string text) =>
        Regex.IsMatch(text, @"\bmerge\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool ContainsShipWord(string text) =>
        Regex.IsMatch(text, @"\bship(?:\s+it)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool ContainsOpenPullRequestPhrase(string text) =>
        Regex.IsMatch(
            text,
            @"\b(?:open|prepare)(?:\s+a)?\s+pr\b|\bready for review\b|\bopen pull request\b|\bprepare a pull request\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
