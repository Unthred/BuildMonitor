using System.Text.RegularExpressions;

namespace BuildMonitor.Infrastructure.LocalBuild;

public sealed record DotNetTestSummary(
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    string? DurationText,
    string? AssemblyName);

public static class DotNetTestOutputParser
{
    //   Failed MyTests.Class.Method [12 ms]
    private static readonly Regex VstestFailedLineRegex = new(
        @"^\s*Failed\s+(.+?)\s+\[",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex VstestPassedLineRegex = new(
        @"^\s*Passed\s+.+?\s+\[",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex VstestSkippedLineRegex = new(
        @"^\s*Skipped\s+(.+?)\s+\[",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // [xUnit.net 00:00:01.23]     MyTests.Class.Method [FAIL]
    private static readonly Regex XUnitFailLineRegex = new(
        @"\[xUnit\.net[^\]]*\]\s+(.+?)\s+\[FAIL\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex XUnitPassLineRegex = new(
        @"\[xUnit\.net[^\]]*\]\s+.+?\s+\[PASS\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Passed!  - Failed: 0, Passed: 12, Skipped: 0, Total: 12, Duration: 45 ms - BuildMonitor.Tests.dll (net10.0)
    private static readonly Regex VstestSummaryRegex = new(
        @"(?:Passed|Failed)!\s+-\s+Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)(?:,\s*Duration:\s*([^-\r\n]+))?(?:\s*-\s*(.+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Total tests: 12. Passed: 12. Failed: 0. Skipped: 0. Total time: 0.1234 Seconds
    private static readonly Regex LegacySummaryRegex = new(
        @"Total tests:\s*(\d+)\.\s*Passed:\s*(\d+)\.\s*Failed:\s*(\d+)\.\s*Skipped:\s*(\d+)\.\s*Total time:\s*([^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static DotNetTestSummary? TryParseSummary(string logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return null;
        }

        foreach (var line in logText.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var vstest = VstestSummaryRegex.Match(trimmed);
            if (vstest.Success)
            {
                return new DotNetTestSummary(
                    int.Parse(vstest.Groups[4].Value),
                    int.Parse(vstest.Groups[2].Value),
                    int.Parse(vstest.Groups[1].Value),
                    int.Parse(vstest.Groups[3].Value),
                    vstest.Groups[5].Success ? vstest.Groups[5].Value.Trim() : null,
                    vstest.Groups[6].Success ? vstest.Groups[6].Value.Trim() : null);
            }

            var legacy = LegacySummaryRegex.Match(trimmed);
            if (legacy.Success)
            {
                return new DotNetTestSummary(
                    int.Parse(legacy.Groups[1].Value),
                    int.Parse(legacy.Groups[2].Value),
                    int.Parse(legacy.Groups[3].Value),
                    int.Parse(legacy.Groups[4].Value),
                    legacy.Groups[5].Value.Trim(),
                    null);
            }
        }

        return null;
    }

    public static string FormatSummaryLine(DotNetTestSummary summary) =>
        $"{summary.Passed} passed, {summary.Failed} failed, {summary.Skipped} skipped, {summary.Total} total"
        + (string.IsNullOrWhiteSpace(summary.DurationText) ? string.Empty : $", {summary.DurationText.Trim()}");

    /// <summary>
    /// Failed/skipped tests for the log viewer issues panel, plus MSBuild errors when the test host never ran.
    /// </summary>
    public static IReadOnlyList<LogIssue> ParseIssues(string logText, int maxWarnings = 2000)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return [];
        }

        var normalized = logText.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var issues = new List<LogIssue>();
        var claimedLines = new HashSet<int>();
        var skippedCount = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = StripAnsi(lines[i].TrimEnd('\r'));
            if (string.IsNullOrWhiteSpace(line) || IsRunSummaryLine(line))
            {
                continue;
            }

            if (TryParseFailedTest(line, out var failedTestName))
            {
                var summary = ExtractFailureSummary(lines, i);
                var display = string.IsNullOrWhiteSpace(summary)
                    ? failedTestName
                    : $"{failedTestName} — {summary}";
                issues.Add(new LogIssue(i, TruncateDisplay(display), true));
                claimedLines.Add(i);
                continue;
            }

            if (skippedCount < maxWarnings && VstestSkippedLineRegex.IsMatch(line))
            {
                var name = VstestSkippedLineRegex.Match(line).Groups[1].Value.Trim();
                issues.Add(new LogIssue(i, $"Skipped: {name}", false));
                claimedLines.Add(i);
                skippedCount++;
            }
        }

        foreach (var buildIssue in BuildLogParser.ParseIssues(logText, maxWarnings))
        {
            if (!claimedLines.Contains(buildIssue.LineNumber))
            {
                issues.Add(buildIssue);
            }
        }

        return issues
            .OrderBy(issue => issue.LineNumber)
            .ToList();
    }

    public static bool LooksLikeTestsExecuted(string logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return false;
        }

        // Intentionally ignores "Test run for …" — VSTest prints that banner before opening the DLL.
        return HasPostDiscoveryExecutionEvidence(logText);
    }

    public static bool LooksLikeRestoreOrBuildOnly(string logText) =>
        !LooksLikeTestsExecuted(logText)
        && (logText.Contains("(Restore target(s))", StringComparison.OrdinalIgnoreCase)
            || logText.Contains("Done Building Project", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// VSTest named a test assembly then reported it missing — tests never started.
    /// </summary>
    public static bool LooksLikeMissingTestSource(string logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return false;
        }

        var hasNotFound = logText.Contains("was not found", StringComparison.OrdinalIgnoreCase)
                          || logText.Contains("could not be found", StringComparison.OrdinalIgnoreCase);
        if (!hasNotFound)
        {
            return false;
        }

        return logText.Contains("test source file", StringComparison.OrdinalIgnoreCase)
               || logText.Contains("The specified file", StringComparison.OrdinalIgnoreCase)
               || logText.Contains("provided was not found", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when --no-build failed because assemblies are missing or out of date.</summary>
    public static bool LooksLikeNeedsFullBuildBeforeTest(string logText)
    {
        if (string.IsNullOrWhiteSpace(logText) || LooksLikeTestsExecuted(logText))
        {
            return false;
        }

        if (LooksLikeMissingTestSource(logText)
            || LooksLikeRestoreOrBuildOnly(logText)
            || BuildLogParser.IsOutputLockError(logText))
        {
            return true;
        }

        return logText.Contains("has not been built", StringComparison.OrdinalIgnoreCase)
               || logText.Contains("Could not find file", StringComparison.OrdinalIgnoreCase)
               || logText.Contains("could not be found", StringComparison.OrdinalIgnoreCase)
               || logText.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
               || logText.Contains("No test is available in", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Evidence that discovery finished and the test host actually ran cases (or reported a run summary).
    /// Does not include the VSTest "Test run for" banner, which is printed before the DLL is opened.
    /// </summary>
    private static bool HasPostDiscoveryExecutionEvidence(string logText)
    {
        if (TryParseSummary(logText) is { Total: > 0 })
        {
            return true;
        }

        return logText.Contains("Starting test execution", StringComparison.OrdinalIgnoreCase)
               || logText.Contains("Passed!", StringComparison.OrdinalIgnoreCase)
               || logText.Contains("Failed!", StringComparison.OrdinalIgnoreCase)
               || logText.Contains("Total tests:", StringComparison.OrdinalIgnoreCase)
               || logText.Contains("[FAIL]", StringComparison.Ordinal)
               || logText.Contains("[PASS]", StringComparison.Ordinal);
    }

    private static bool TryParseFailedTest(string line, out string testName)
    {
        testName = string.Empty;

        var vstest = VstestFailedLineRegex.Match(line);
        if (vstest.Success)
        {
            testName = vstest.Groups[1].Value.Trim();
            return true;
        }

        var xunit = XUnitFailLineRegex.Match(line);
        if (xunit.Success)
        {
            testName = xunit.Groups[1].Value.Trim();
            return true;
        }

        return false;
    }

    private static string? ExtractFailureSummary(string[] lines, int failedLineIndex)
    {
        var parts = new List<string>();

        for (var j = failedLineIndex + 1; j < Math.Min(failedLineIndex + 24, lines.Length); j++)
        {
            var line = StripAnsi(lines[j].TrimEnd('\r')).Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                if (parts.Count > 0)
                {
                    break;
                }

                continue;
            }

            if (IsNextTestResultLine(line) || IsRunSummaryLine(line))
            {
                break;
            }

            if (IsFailureNoiseLine(line))
            {
                continue;
            }

            if (line.StartsWith("at ", StringComparison.OrdinalIgnoreCase)
                || line.Contains(" in ", StringComparison.Ordinal) && line.Contains(":line ", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            parts.Add(line);
            if (parts.Count >= 4)
            {
                break;
            }
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static bool IsNextTestResultLine(string line) =>
        VstestFailedLineRegex.IsMatch(line)
        || VstestPassedLineRegex.IsMatch(line)
        || VstestSkippedLineRegex.IsMatch(line)
        || XUnitFailLineRegex.IsMatch(line)
        || XUnitPassLineRegex.IsMatch(line);

    private static bool IsRunSummaryLine(string line) =>
        VstestSummaryRegex.IsMatch(line.Trim())
        || LegacySummaryRegex.IsMatch(line.Trim())
        || line.Trim().Equals("Test Run Failed.", StringComparison.OrdinalIgnoreCase)
        || line.Trim().Equals("Test Run Successful.", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailureNoiseLine(string line) =>
        line.Equals("Error Message:", StringComparison.OrdinalIgnoreCase)
        || line.Equals("Message:", StringComparison.OrdinalIgnoreCase)
        || line.Equals("Stack Trace:", StringComparison.OrdinalIgnoreCase)
        || line.Equals("Standard Output Messages:", StringComparison.OrdinalIgnoreCase);

    private static string TruncateDisplay(string text) =>
        text.Length <= 320 ? text : text[..317] + "...";

    private static string StripAnsi(string line) =>
        Regex.Replace(line, @"\x1b\[[0-9;]*m", string.Empty);
}
