using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class ControlPlaneStatusFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Format_returns_hidden_when_control_plane_never_used()
    {
        var snapshot = CreateSnapshot(ProjectControlPlaneSnapshot.Unused);

        var presentation = ControlPlaneStatusFormatter.Format(snapshot, Now);

        Assert.False(presentation.ShowControlPlaneSection);
        Assert.Null(presentation.AgentPrimary);
        Assert.Equal("File Watching", presentation.ModePrimary);
    }

    [Fact]
    public void Format_ai_controlled_busy_uses_agent_editing_not_builds_paused()
    {
        var controlPlane = new ProjectControlPlaneSnapshot(
            SessionApiUsed: true,
            EffectiveSessionState: ControlPlaneSessionState.Busy,
            SessionSinceUtc: Now.AddSeconds(-20),
            AutoBuildBlockedBySession: false,
            HasPendingFileChangeRebuild: true,
            PendingFileChangeCount: 7,
            ShipCheckPhase: ControlPlaneShipCheckPhase.None,
            LastShipCheckOutcome: ControlPlaneShipCheckOutcome.None,
            LastShipCheckCompletedUtc: null,
            ShipCheckInProgress: false,
            BuildControlMode: ProjectBuildControlMode.AiControlled,
            AutoBuildEnabled: false);

        var presentation = ControlPlaneStatusFormatter.Format(CreateSnapshot(controlPlane), Now);

        Assert.Equal("AI Controlled", presentation.ModePrimary);
        Assert.Equal("Busy", presentation.AgentPrimary);
        Assert.Contains("Editing", presentation.AgentSecondary);
        Assert.DoesNotContain("Builds paused", presentation.AgentSecondary);
        Assert.Equal("7 detected", presentation.ChangesPrimary);
        Assert.Equal("Awaiting agent", presentation.ChangesSecondary);
    }

    [Fact]
    public void Format_ai_controlled_idle_with_pending_awaits_explicit_build()
    {
        var controlPlane = new ProjectControlPlaneSnapshot(
            SessionApiUsed: true,
            EffectiveSessionState: ControlPlaneSessionState.Idle,
            SessionSinceUtc: Now.AddSeconds(-5),
            AutoBuildBlockedBySession: false,
            HasPendingFileChangeRebuild: true,
            PendingFileChangeCount: 7,
            ShipCheckPhase: ControlPlaneShipCheckPhase.None,
            LastShipCheckOutcome: ControlPlaneShipCheckOutcome.None,
            LastShipCheckCompletedUtc: null,
            ShipCheckInProgress: false,
            IdleCause: ControlPlaneIdleCause.Agent,
            BuildControlMode: ProjectBuildControlMode.AiControlled,
            AutoBuildEnabled: false);

        var presentation = ControlPlaneStatusFormatter.Format(CreateSnapshot(controlPlane), Now);

        Assert.Equal("Idle", presentation.AgentPrimary);
        Assert.Equal("Editing finished", presentation.AgentSecondary);
        Assert.Equal("7 detected", presentation.ChangesPrimary);
        Assert.Equal("Awaiting explicit build", presentation.ChangesSecondary);
    }

    [Fact]
    public void Format_ai_controlled_timeout_awaits_explicit_build()
    {
        var controlPlane = new ProjectControlPlaneSnapshot(
            SessionApiUsed: true,
            EffectiveSessionState: ControlPlaneSessionState.Idle,
            SessionSinceUtc: Now.AddSeconds(-5),
            AutoBuildBlockedBySession: false,
            HasPendingFileChangeRebuild: true,
            PendingFileChangeCount: 2,
            ShipCheckPhase: ControlPlaneShipCheckPhase.None,
            LastShipCheckOutcome: ControlPlaneShipCheckOutcome.None,
            LastShipCheckCompletedUtc: null,
            ShipCheckInProgress: false,
            IdleCause: ControlPlaneIdleCause.Timeout,
            BuildControlMode: ProjectBuildControlMode.AiControlled,
            AutoBuildEnabled: false);

        var presentation = ControlPlaneStatusFormatter.Format(CreateSnapshot(controlPlane), Now);

        Assert.Equal("Agent session ended", presentation.AgentSecondary);
        Assert.Equal("Awaiting explicit build", presentation.ChangesSecondary);
    }

    [Fact]
    public void Format_busy_shows_builds_paused_and_queued_changes()
    {
        var controlPlane = new ProjectControlPlaneSnapshot(
            SessionApiUsed: true,
            EffectiveSessionState: ControlPlaneSessionState.Busy,
            SessionSinceUtc: Now.AddSeconds(-43),
            AutoBuildBlockedBySession: true,
            HasPendingFileChangeRebuild: true,
            PendingFileChangeCount: 3,
            ShipCheckPhase: ControlPlaneShipCheckPhase.None,
            LastShipCheckOutcome: ControlPlaneShipCheckOutcome.None,
            LastShipCheckCompletedUtc: null,
            ShipCheckInProgress: false);

        var presentation = ControlPlaneStatusFormatter.Format(CreateSnapshot(controlPlane), Now);

        Assert.Equal("Busy", presentation.AgentPrimary);
        Assert.Equal("Builds paused · 43s", presentation.AgentSecondary);
        Assert.Equal("3 queued", presentation.ChangesPrimary);
        Assert.Equal(StatusPanelRowEmphasis.Busy, presentation.AgentEmphasis);
    }

    [Fact]
    public void Format_idle_after_session_shows_connected()
    {
        var controlPlane = new ProjectControlPlaneSnapshot(
            SessionApiUsed: true,
            EffectiveSessionState: ControlPlaneSessionState.Idle,
            SessionSinceUtc: Now.AddMinutes(-5),
            AutoBuildBlockedBySession: false,
            HasPendingFileChangeRebuild: false,
            PendingFileChangeCount: 0,
            ShipCheckPhase: ControlPlaneShipCheckPhase.None,
            LastShipCheckOutcome: ControlPlaneShipCheckOutcome.None,
            LastShipCheckCompletedUtc: null,
            ShipCheckInProgress: false);

        var presentation = ControlPlaneStatusFormatter.Format(CreateSnapshot(controlPlane), Now);

        Assert.Equal("Idle", presentation.AgentPrimary);
        Assert.Equal("Build allowed", presentation.AgentSecondary);
    }

    [Fact]
    public void Format_recent_idle_transition_shows_build_allowed()
    {
        var controlPlane = new ProjectControlPlaneSnapshot(
            SessionApiUsed: true,
            EffectiveSessionState: ControlPlaneSessionState.Idle,
            SessionSinceUtc: Now.AddSeconds(-10),
            AutoBuildBlockedBySession: false,
            HasPendingFileChangeRebuild: true,
            PendingFileChangeCount: 2,
            ShipCheckPhase: ControlPlaneShipCheckPhase.None,
            LastShipCheckOutcome: ControlPlaneShipCheckOutcome.None,
            LastShipCheckCompletedUtc: null,
            ShipCheckInProgress: false);

        var presentation = ControlPlaneStatusFormatter.Format(CreateSnapshot(controlPlane), Now);

        Assert.Equal("Idle", presentation.AgentPrimary);
        Assert.Equal("Build allowed", presentation.AgentSecondary);
        Assert.Equal("2 queued", presentation.ChangesPrimary);
    }

    [Fact]
    public void Format_ship_check_build_phase()
    {
        var controlPlane = BusyControlPlane with
        {
            ShipCheckPhase = ControlPlaneShipCheckPhase.Building,
            ShipCheckInProgress = true,
            EffectiveSessionState = ControlPlaneSessionState.Idle,
            AutoBuildBlockedBySession = false
        };

        var presentation = ControlPlaneStatusFormatter.Format(CreateSnapshot(controlPlane), Now);

        Assert.Equal("Ship check · Building", presentation.BuildActivityOverride);
        Assert.Equal("Compiling…", presentation.TransientAction);
    }

    [Fact]
    public void Format_ship_check_test_phase()
    {
        var controlPlane = BusyControlPlane with
        {
            ShipCheckPhase = ControlPlaneShipCheckPhase.Testing,
            ShipCheckInProgress = true
        };

        var presentation = ControlPlaneStatusFormatter.Format(CreateSnapshot(controlPlane), Now);

        Assert.Equal("Ship check · Testing", presentation.BuildActivityOverride);
        Assert.Equal("Running tests…", presentation.TransientAction);
    }

    [Fact]
    public void Format_ship_check_passed()
    {
        var controlPlane = new ProjectControlPlaneSnapshot(
            SessionApiUsed: true,
            EffectiveSessionState: ControlPlaneSessionState.Idle,
            SessionSinceUtc: Now.AddMinutes(-1),
            AutoBuildBlockedBySession: false,
            HasPendingFileChangeRebuild: false,
            PendingFileChangeCount: 0,
            ShipCheckPhase: ControlPlaneShipCheckPhase.None,
            LastShipCheckOutcome: ControlPlaneShipCheckOutcome.Passed,
            LastShipCheckCompletedUtc: Now.AddSeconds(-15),
            ShipCheckInProgress: false);

        var presentation = ControlPlaneStatusFormatter.Format(CreateSnapshot(controlPlane), Now);

        Assert.Equal("Ship check passed", presentation.BuildActivityOverride);
    }

    [Fact]
    public void Format_ship_check_failed()
    {
        var controlPlane = new ProjectControlPlaneSnapshot(
            SessionApiUsed: true,
            EffectiveSessionState: ControlPlaneSessionState.Idle,
            SessionSinceUtc: Now.AddMinutes(-1),
            AutoBuildBlockedBySession: false,
            HasPendingFileChangeRebuild: false,
            PendingFileChangeCount: 0,
            ShipCheckPhase: ControlPlaneShipCheckPhase.None,
            LastShipCheckOutcome: ControlPlaneShipCheckOutcome.Failed,
            LastShipCheckCompletedUtc: Now.AddSeconds(-20),
            ShipCheckInProgress: false);

        var presentation = ControlPlaneStatusFormatter.Format(CreateSnapshot(controlPlane), Now);

        Assert.Equal("Ship check failed", presentation.BuildActivityOverride);
    }

    [Fact]
    public void Format_rebuild_build_phase()
    {
        var controlPlane = BusyControlPlane with
        {
            AgentRebuildInProgress = true,
            AgentRebuildPhase = ControlPlaneShipCheckPhase.Building,
            EffectiveSessionState = ControlPlaneSessionState.Idle,
            AutoBuildBlockedBySession = false
        };

        var presentation = ControlPlaneStatusFormatter.Format(CreateSnapshot(controlPlane), Now);

        Assert.Equal("Agent rebuild · Building", presentation.BuildActivityOverride);
        Assert.Equal("Rebuilding…", presentation.TransientAction);
    }

    [Fact]
    public void Format_rebuild_passed()
    {
        var controlPlane = new ProjectControlPlaneSnapshot(
            SessionApiUsed: true,
            EffectiveSessionState: ControlPlaneSessionState.Idle,
            SessionSinceUtc: Now.AddMinutes(-1),
            AutoBuildBlockedBySession: false,
            HasPendingFileChangeRebuild: false,
            PendingFileChangeCount: 0,
            ShipCheckPhase: ControlPlaneShipCheckPhase.None,
            LastShipCheckOutcome: ControlPlaneShipCheckOutcome.None,
            LastShipCheckCompletedUtc: null,
            ShipCheckInProgress: false,
            AgentRebuildInProgress: false,
            AgentRebuildPhase: ControlPlaneShipCheckPhase.None,
            LastAgentRebuildOutcome: ControlPlaneShipCheckOutcome.Passed,
            LastAgentRebuildCompletedUtc: Now.AddSeconds(-10));

        var presentation = ControlPlaneStatusFormatter.Format(CreateSnapshot(controlPlane), Now);

        Assert.Equal("Rebuild passed", presentation.BuildActivityOverride);
    }

    [Fact]
    public void Format_timeout_idle_is_distinct_from_agent_idle()
    {
        var controlPlane = new ProjectControlPlaneSnapshot(
            SessionApiUsed: true,
            EffectiveSessionState: ControlPlaneSessionState.Idle,
            SessionSinceUtc: Now.AddSeconds(-10),
            AutoBuildBlockedBySession: false,
            HasPendingFileChangeRebuild: true,
            PendingFileChangeCount: 2,
            ShipCheckPhase: ControlPlaneShipCheckPhase.None,
            LastShipCheckOutcome: ControlPlaneShipCheckOutcome.None,
            LastShipCheckCompletedUtc: null,
            ShipCheckInProgress: false,
            IdleCause: ControlPlaneIdleCause.Timeout);

        var presentation = ControlPlaneStatusFormatter.Format(CreateSnapshot(controlPlane), Now);

        Assert.Equal("Idle", presentation.AgentPrimary);
        Assert.Equal("Timed out · build allowed", presentation.AgentSecondary);
        Assert.Equal("2 queued", presentation.ChangesPrimary);
    }

    [Fact]
    public void Format_tests_running()
    {
        var controlPlane = BusyControlPlane with
        {
            AgentTestsInProgress = true,
            EffectiveSessionState = ControlPlaneSessionState.Idle,
            AutoBuildBlockedBySession = false
        };

        var presentation = ControlPlaneStatusFormatter.Format(CreateSnapshot(controlPlane), Now);

        Assert.Equal("Tests", presentation.BuildActivityOverride);
        Assert.Equal("Running tests…", presentation.TransientAction);
    }

    private static ProjectControlPlaneSnapshot BusyControlPlane => new(
        SessionApiUsed: true,
        EffectiveSessionState: ControlPlaneSessionState.Busy,
        SessionSinceUtc: Now.AddSeconds(-30),
        AutoBuildBlockedBySession: true,
        HasPendingFileChangeRebuild: false,
        PendingFileChangeCount: 0,
        ShipCheckPhase: ControlPlaneShipCheckPhase.None,
        LastShipCheckOutcome: ControlPlaneShipCheckOutcome.None,
        LastShipCheckCompletedUtc: null,
        ShipCheckInProgress: false);

    private static ProjectHealthSnapshot CreateSnapshot(ProjectControlPlaneSnapshot controlPlane) =>
        new(
            ProjectId: "demo",
            DisplayName: "Demo",
            Health: MonitorHealth.Green,
            HealthLabel: "Healthy",
            State: ProjectLifecycleState.Watching,
            LastExitCode: 0,
            LastDuration: TimeSpan.FromSeconds(10),
            LastErrorPreview: null,
            ErrorCount: 0,
            WarningCount: 0,
            LastChangedUtc: Now,
            LastBuildFinishedAtUtc: Now.AddMinutes(-5),
            IsActive: true,
            ProgressSteps: [],
            ControlPlane: controlPlane);
}
