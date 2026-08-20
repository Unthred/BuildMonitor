using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

/// <summary>
/// Integration-style policy sequence for the deterministic AI agent workflow.
/// </summary>
public sealed class AiControlledWorkflowPolicyTests
{
    [Fact]
    public void File_watching_to_ai_controlled_busy_idle_never_auto_builds_until_explicit()
    {
        var mode = ProjectBuildControlMode.FileWatching;
        var autoBuildSchedules = 0;
        var explicitRebuilds = 0;

        void OnFileChange(bool sessionApiUsed, ControlPlaneSessionState state)
        {
            if (BuildTriggerPolicy.ShouldAutoBuildFromFileChange(mode, sessionApiUsed, state))
            {
                autoBuildSchedules++;
            }
        }

        void OnExplicitRebuild() => explicitRebuilds++;

        // Switch to AI Controlled (agent start)
        mode = ProjectBuildControlMode.AiControlled;

        // Busy + several file changes
        OnFileChange(sessionApiUsed: true, ControlPlaneSessionState.Busy);
        OnFileChange(sessionApiUsed: true, ControlPlaneSessionState.Busy);
        OnFileChange(sessionApiUsed: true, ControlPlaneSessionState.Busy);
        Assert.Equal(0, autoBuildSchedules);

        // Idle — must not schedule
        OnFileChange(sessionApiUsed: true, ControlPlaneSessionState.Idle);
        Assert.Equal(0, autoBuildSchedules);

        // Busy timeout → Idle — still no auto-build
        Assert.False(BuildTriggerPolicy.BusyTimeoutMayResumeAutoBuild(mode));
        OnFileChange(sessionApiUsed: true, ControlPlaneSessionState.Idle);
        Assert.Equal(0, autoBuildSchedules);

        OnExplicitRebuild();
        Assert.Equal(1, explicitRebuilds);
        Assert.Equal(0, autoBuildSchedules);
    }

    [Fact]
    public void Same_flow_ending_in_ship_check_still_zero_auto_builds()
    {
        var mode = ProjectBuildControlMode.AiControlled;
        var autoBuildSchedules = 0;
        var shipChecks = 0;

        for (var i = 0; i < 5; i++)
        {
            if (BuildTriggerPolicy.ShouldAutoBuildFromFileChange(
                    mode, sessionApiUsed: true, ControlPlaneSessionState.Busy))
            {
                autoBuildSchedules++;
            }
        }

        if (BuildTriggerPolicy.ShouldAutoBuildFromFileChange(
                mode, sessionApiUsed: true, ControlPlaneSessionState.Idle))
        {
            autoBuildSchedules++;
        }

        shipChecks++;
        Assert.Equal(0, autoBuildSchedules);
        Assert.Equal(1, shipChecks);
    }
}
