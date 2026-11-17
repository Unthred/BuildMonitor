using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class StartupBuildPolicyTests
{
    private static readonly DateTimeOffset BuildStarted = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OutputWritten = BuildStarted.AddMinutes(2);

    private static StartupBuildInputs UpToDateAi() => new(
        ProjectBuildControlMode.AiControlled,
        LastBuildExitCode: 0,
        LastBuildStartedAtUtc: BuildStarted,
        BuildOutputWrittenUtc: OutputWritten,
        NewestSourceWriteUtc: BuildStarted.AddMinutes(-10));

    [Fact]
    public void Ai_controlled_with_successful_build_and_no_newer_sources_skips()
    {
        var decision = StartupBuildPolicy.Decide(UpToDateAi());

        Assert.False(decision.ShouldBuild);
        Assert.Equal(StartupBuildDecisionKind.SkipUpToDate, decision.Kind);
    }

    [Fact]
    public void Ai_controlled_with_no_source_files_found_skips()
    {
        var decision = StartupBuildPolicy.Decide(UpToDateAi() with { NewestSourceWriteUtc = null });

        Assert.False(decision.ShouldBuild);
    }

    [Fact]
    public void File_watching_always_builds()
    {
        var decision = StartupBuildPolicy.Decide(
            UpToDateAi() with { BuildControlMode = ProjectBuildControlMode.FileWatching });

        Assert.True(decision.ShouldBuild);
        Assert.Equal("File Watching builds on startup", decision.Reason);
    }

    [Fact]
    public void No_saved_build_builds()
    {
        var decision = StartupBuildPolicy.Decide(
            UpToDateAi() with { LastBuildExitCode = null, LastBuildStartedAtUtc = null });

        Assert.True(decision.ShouldBuild);
        Assert.Equal("no saved build", decision.Reason);
    }

    [Fact]
    public void Failed_last_build_builds()
    {
        var decision = StartupBuildPolicy.Decide(UpToDateAi() with { LastBuildExitCode = 1 });

        Assert.True(decision.ShouldBuild);
        Assert.Equal("last build failed", decision.Reason);
    }

    [Fact]
    public void Missing_output_builds()
    {
        var decision = StartupBuildPolicy.Decide(UpToDateAi() with { BuildOutputWrittenUtc = null });

        Assert.True(decision.ShouldBuild);
        Assert.Equal("build output missing", decision.Reason);
    }

    [Fact]
    public void Source_saved_while_last_build_ran_builds()
    {
        // Saved after the build started but before the output was written.
        var decision = StartupBuildPolicy.Decide(
            UpToDateAi() with { NewestSourceWriteUtc = BuildStarted.AddMinutes(1) });

        Assert.True(decision.ShouldBuild);
        Assert.Equal("sources changed since last build", decision.Reason);
    }

    [Fact]
    public void Output_older_than_last_build_uses_output_time()
    {
        // Last build was a Release ship-check at 09:00; the Debug output the host runs from
        // dates from 08:00 and a source changed at 08:30.
        var decision = StartupBuildPolicy.Decide(UpToDateAi() with
        {
            BuildOutputWrittenUtc = BuildStarted.AddHours(-1),
            NewestSourceWriteUtc = BuildStarted.AddMinutes(-30)
        });

        Assert.True(decision.ShouldBuild);
        Assert.Equal("sources changed since last build", decision.Reason);
    }

    [Fact]
    public void Source_written_exactly_at_build_start_builds()
    {
        var decision = StartupBuildPolicy.Decide(
            UpToDateAi() with { NewestSourceWriteUtc = BuildStarted });

        Assert.True(decision.ShouldBuild);
    }
}
