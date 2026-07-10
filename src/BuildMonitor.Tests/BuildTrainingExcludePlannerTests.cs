using BuildMonitor.Core.Rules;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class BuildTrainingExcludePlannerTests
{
    [Fact]
    public void SuggestExcludeSegments_prefers_cursor_for_rule_files()
    {
        var excluded = WatchExcludeSegments.ResolveIgnoreSegmentSet(null);

        var suggestions = BuildTrainingExcludePlanner.SuggestExcludeSegments(
            [".cursor/rules/work-tracking.mdc"],
            excluded);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void SuggestExcludeSegments_suggests_docs_when_not_already_excluded()
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "src", "bin", "obj", ".cursor", "agent-transcripts"
        };

        var suggestions = BuildTrainingExcludePlanner.SuggestExcludeSegments(
            ["docs/SETTINGS.md"],
            excluded);

        Assert.Single(suggestions);
        Assert.Equal("docs", suggestions[0]);
    }

    [Fact]
    public void SuggestExcludeSegments_skips_compile_source_paths()
    {
        var excluded = WatchExcludeSegments.ResolveIgnoreSegmentSet(null);

        var suggestions = BuildTrainingExcludePlanner.SuggestExcludeSegments(
            ["src/HomeController.cs"],
            excluded);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void SuggestExcludeSegments_suggests_tooling_segment_when_missing_from_excludes()
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "src", "bin", "obj" };

        var suggestions = BuildTrainingExcludePlanner.SuggestExcludeSegments(
            [".cursor/plans/feature.md"],
            excluded);

        Assert.Single(suggestions);
        Assert.Equal(".cursor", suggestions[0]);
    }
}
