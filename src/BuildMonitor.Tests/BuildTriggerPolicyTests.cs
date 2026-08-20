using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class BuildTriggerPolicyTests
{
    [Theory]
    [InlineData(ProjectBuildControlMode.FileWatching, false, ControlPlaneSessionState.Idle, true)]
    [InlineData(ProjectBuildControlMode.FileWatching, true, ControlPlaneSessionState.Idle, true)]
    [InlineData(ProjectBuildControlMode.FileWatching, true, ControlPlaneSessionState.Busy, false)]
    [InlineData(ProjectBuildControlMode.AiControlled, false, ControlPlaneSessionState.Idle, false)]
    [InlineData(ProjectBuildControlMode.AiControlled, true, ControlPlaneSessionState.Idle, false)]
    [InlineData(ProjectBuildControlMode.AiControlled, true, ControlPlaneSessionState.Busy, false)]
    public void ShouldAutoBuildFromFileChange_matches_mode_and_session(
        ProjectBuildControlMode mode,
        bool sessionApiUsed,
        ControlPlaneSessionState state,
        bool expected)
    {
        Assert.Equal(
            expected,
            BuildTriggerPolicy.ShouldAutoBuildFromFileChange(mode, sessionApiUsed, state));
    }

    [Fact]
    public void FileWatching_busy_timeout_may_resume_auto_build()
    {
        Assert.True(BuildTriggerPolicy.BusyTimeoutMayResumeAutoBuild(ProjectBuildControlMode.FileWatching));
        Assert.True(
            BuildTriggerPolicy.ShouldAutoBuildFromFileChange(
                ProjectBuildControlMode.FileWatching,
                sessionApiUsed: true,
                ControlPlaneSessionState.Idle));
    }

    [Fact]
    public void AiControlled_busy_timeout_must_not_resume_auto_build()
    {
        Assert.False(BuildTriggerPolicy.BusyTimeoutMayResumeAutoBuild(ProjectBuildControlMode.AiControlled));
        Assert.False(
            BuildTriggerPolicy.ShouldAutoBuildFromFileChange(
                ProjectBuildControlMode.AiControlled,
                sessionApiUsed: true,
                ControlPlaneSessionState.Idle));
        Assert.True(BuildTriggerPolicy.IsAutoBuildDisabledByMode(ProjectBuildControlMode.AiControlled));
    }

    [Theory]
    [InlineData("file-watching", ProjectBuildControlMode.FileWatching)]
    [InlineData("ai-controlled", ProjectBuildControlMode.AiControlled)]
    [InlineData("AI-CONTROLLED", ProjectBuildControlMode.AiControlled)]
    public void Wire_parse_accepts_stable_values(string wire, ProjectBuildControlMode expected)
    {
        Assert.True(ProjectBuildControlModeWire.TryParse(wire, out var mode));
        Assert.Equal(expected, mode);
        Assert.Equal(
            expected == ProjectBuildControlMode.AiControlled
                ? ProjectBuildControlModeWire.AiControlled
                : ProjectBuildControlModeWire.FileWatching,
            ProjectBuildControlModeWire.ToWire(mode));
    }

    [Fact]
    public void Wire_parse_rejects_invalid()
    {
        Assert.False(ProjectBuildControlModeWire.TryParse("hybrid", out _));
        Assert.False(ProjectBuildControlModeWire.TryParse("", out _));
    }
}
