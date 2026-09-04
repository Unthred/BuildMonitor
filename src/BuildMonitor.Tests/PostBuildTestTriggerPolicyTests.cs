using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class PostBuildTestTriggerPolicyTests
{
    public static TheoryData<TestRunTrigger, bool, bool, bool> DecisionMatrix => new()
    {
        // runTests, triggeredByFileChange, skipAutoBuildTests, expected
        { TestRunTrigger.Off, true, false, false },
        { TestRunTrigger.Off, false, false, false },
        { TestRunTrigger.OnBuildSuccess, true, false, true },
        { TestRunTrigger.OnBuildSuccess, false, false, true },
        { TestRunTrigger.OnBuildSuccess, true, true, false },
        { TestRunTrigger.OnBuildSuccess, false, true, false },
        { TestRunTrigger.OnFileChange, true, false, true },
        { TestRunTrigger.OnFileChange, false, false, false },
        { TestRunTrigger.OnFileChange, true, true, false },
        { TestRunTrigger.OnFileChange, false, true, false },
    };

    [Theory]
    [MemberData(nameof(DecisionMatrix))]
    public void Decision_matrix(
        TestRunTrigger runTests,
        bool triggeredByFileChange,
        bool skipAutoBuildTests,
        bool expected)
    {
        var actual = PostBuildTestTriggerPolicy.ShouldRunTestsAfterSuccessfulBuild(
            runTests,
            triggeredByFileChange,
            skipAutoBuildTests);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void OnFileChange_file_watching_suppress_false_runs_once_equivalent()
    {
        // File Watching + OnFileChange + suppress false → exactly one eligible decision.
        Assert.True(PostBuildTestTriggerPolicy.ShouldRunTestsAfterSuccessfulBuild(
            TestRunTrigger.OnFileChange,
            triggeredByFileChange: true,
            skipAutoBuildTests: false));
    }

    [Fact]
    public void OnFileChange_suppress_true_skips()
    {
        Assert.False(PostBuildTestTriggerPolicy.ShouldRunTestsAfterSuccessfulBuild(
            TestRunTrigger.OnFileChange,
            triggeredByFileChange: true,
            skipAutoBuildTests: true));
    }

    [Fact]
    public void OnFileChange_manual_agent_startup_rebuild_origins_do_not_run()
    {
        // Non-file origins: triggeredByFileChange == false (manual, /run/rebuild, agent, startup, ship-check build).
        Assert.False(PostBuildTestTriggerPolicy.ShouldRunTestsAfterSuccessfulBuild(
            TestRunTrigger.OnFileChange,
            triggeredByFileChange: false,
            skipAutoBuildTests: false));
    }

    [Fact]
    public void OnBuildSuccess_still_runs_for_non_file_builds_when_not_suppressed()
    {
        Assert.True(PostBuildTestTriggerPolicy.ShouldRunTestsAfterSuccessfulBuild(
            TestRunTrigger.OnBuildSuccess,
            triggeredByFileChange: false,
            skipAutoBuildTests: false));
    }

    [Fact]
    public void Ship_check_build_does_not_satisfy_OnFileChange()
    {
        // Ship-check uses PrepareTest("ship-check") explicitly; build origin is not file-triggered.
        Assert.False(PostBuildTestTriggerPolicy.ShouldRunTestsAfterSuccessfulBuild(
            TestRunTrigger.OnFileChange,
            triggeredByFileChange: false,
            skipAutoBuildTests: true)); // ship-check also sets skip via shipCheckInProgress
    }
}

public sealed class TestRunTriggerDisplayTests
{
    [Fact]
    public void Persisted_enum_values_unchanged()
    {
        Assert.Equal(0, (int)TestRunTrigger.Off);
        Assert.Equal(1, (int)TestRunTrigger.OnBuildSuccess);
        Assert.Equal(2, (int)TestRunTrigger.OnFileChange);
    }

    [Fact]
    public void OnFileChange_label_is_after_file_triggered_build()
    {
        Assert.Equal("After file-triggered build", TestRunTriggerDisplay.ToLabel(TestRunTrigger.OnFileChange));
        Assert.Contains("file-triggered", TestRunTriggerDisplay.HelpText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AI Controlled", TestRunTriggerDisplay.HelpText, StringComparison.Ordinal);
        Assert.Contains("Suppress", TestRunTriggerDisplay.HelpText, StringComparison.Ordinal);
    }
}

public sealed class AiControlledOnFileChangeRegressionTests
{
    [Fact]
    public void Ai_controlled_plus_OnFileChange_never_auto_builds_or_auto_tests_from_edits()
    {
        const ProjectBuildControlMode mode = ProjectBuildControlMode.AiControlled;
        var autoBuilds = 0;
        var autoTests = 0;

        void OnSourceEdit()
        {
            var mayBuild = BuildTriggerPolicy.ShouldAutoBuildFromFileChange(
                mode,
                sessionApiUsed: true,
                ControlPlaneSessionState.Idle);
            if (mayBuild)
            {
                autoBuilds++;
                // Only a permitted file-triggered build could feed OnFileChange.
                if (PostBuildTestTriggerPolicy.ShouldRunTestsAfterSuccessfulBuild(
                        TestRunTrigger.OnFileChange,
                        triggeredByFileChange: true,
                        skipAutoBuildTests: false))
                {
                    autoTests++;
                }
            }
            else
            {
                // No file-triggered build occurred — OnFileChange must not invent a test run.
                Assert.False(PostBuildTestTriggerPolicy.ShouldRunTestsAfterSuccessfulBuild(
                    TestRunTrigger.OnFileChange,
                    triggeredByFileChange: false,
                    skipAutoBuildTests: false));
            }
        }

        OnSourceEdit();
        OnSourceEdit();
        OnSourceEdit();

        Assert.Equal(0, autoBuilds);
        Assert.Equal(0, autoTests);
        Assert.True(BuildTriggerPolicy.IsAutoBuildDisabledByMode(mode));
    }
}
