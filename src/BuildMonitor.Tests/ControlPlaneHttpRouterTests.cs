using System.Text;
using BuildMonitor.Core.Models;
using BuildMonitor.Infrastructure.ControlPlane;

namespace BuildMonitor.Tests;

public sealed class ControlPlaneHttpRouterTests
{
    [Fact]
    public async Task Get_projects_does_not_require_projectId()
    {
        var actions = new FakeActions();
        var response = await ControlPlaneHttpRouter.DispatchAsync(
            actions,
            "GET",
            new Uri("http://127.0.0.1:7700/projects"),
            new MemoryStream(),
            Encoding.UTF8,
            CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        Assert.True(actions.ListCalled);
    }

    [Fact]
    public async Task Get_session_requires_projectId()
    {
        var actions = new FakeActions();
        var response = await ControlPlaneHttpRouter.DispatchAsync(
            actions,
            "GET",
            new Uri("http://127.0.0.1:7700/session"),
            new MemoryStream(),
            Encoding.UTF8,
            CancellationToken.None);

        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task Post_busy_marks_session()
    {
        var actions = new FakeActions { Exists = true };
        var body = Encoding.UTF8.GetBytes("""{"projectId":"abc","suppressAutoBuildTests":false}""");
        var response = await ControlPlaneHttpRouter.DispatchAsync(
            actions,
            "POST",
            new Uri("http://127.0.0.1:7700/session/busy"),
            new MemoryStream(body),
            Encoding.UTF8,
            CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("abc", actions.LastBusyProjectId);
        Assert.False(actions.LastBusySuppress);
    }

    [Fact]
    public async Task Post_rebuild_marks_idle_and_runs_rebuild()
    {
        var actions = new FakeActions { Exists = true };
        var body = Encoding.UTF8.GetBytes("""{"projectId":"abc","configuration":"Debug"}""");
        var response = await ControlPlaneHttpRouter.DispatchAsync(
            actions,
            "POST",
            new Uri("http://127.0.0.1:7700/run/rebuild"),
            new MemoryStream(body),
            Encoding.UTF8,
            CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("abc", actions.LastRebuildProjectId);
    }

    [Fact]
    public async Task Post_tests_runs_tests()
    {
        var actions = new FakeActions { Exists = true };
        var body = Encoding.UTF8.GetBytes("""{"projectId":"abc","filter":"FullyQualifiedName~Foo"}""");
        var response = await ControlPlaneHttpRouter.DispatchAsync(
            actions,
            "POST",
            new Uri("http://127.0.0.1:7700/run/tests"),
            new MemoryStream(body),
            Encoding.UTF8,
            CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public async Task Post_run_stop_stops_running_app()
    {
        var actions = new FakeActions { Exists = true };
        var body = Encoding.UTF8.GetBytes("""{"projectId":"abc"}""");
        var response = await ControlPlaneHttpRouter.DispatchAsync(
            actions,
            "POST",
            new Uri("http://127.0.0.1:7700/run/stop"),
            new MemoryStream(body),
            Encoding.UTF8,
            CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("abc", actions.LastStopProjectId);
    }

    [Fact]
    public async Task Get_mode_returns_wire_value()
    {
        var actions = new FakeActions { Exists = true, Mode = ProjectBuildControlMode.FileWatching };
        var response = await ControlPlaneHttpRouter.DispatchAsync(
            actions,
            "GET",
            new Uri("http://127.0.0.1:7700/mode?projectId=abc"),
            new MemoryStream(),
            Encoding.UTF8,
            CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        var json = System.Text.Json.JsonSerializer.Serialize(response.Body);
        Assert.Contains("file-watching", json, StringComparison.Ordinal);
        Assert.Contains("abc", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_mode_returns_previous_and_current()
    {
        var actions = new FakeActions { Exists = true, Mode = ProjectBuildControlMode.FileWatching };
        var body = Encoding.UTF8.GetBytes("""{"projectId":"abc","mode":"ai-controlled"}""");
        var response = await ControlPlaneHttpRouter.DispatchAsync(
            actions,
            "POST",
            new Uri("http://127.0.0.1:7700/mode"),
            new MemoryStream(body),
            Encoding.UTF8,
            CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(ProjectBuildControlMode.AiControlled, actions.Mode);
        var json = System.Text.Json.JsonSerializer.Serialize(response.Body);
        Assert.Contains("previousMode", json, StringComparison.Ordinal);
        Assert.Contains("file-watching", json, StringComparison.Ordinal);
        Assert.Contains("ai-controlled", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_mode_invalid_returns_400()
    {
        var actions = new FakeActions { Exists = true };
        var body = Encoding.UTF8.GetBytes("""{"projectId":"abc","mode":"hybrid"}""");
        var response = await ControlPlaneHttpRouter.DispatchAsync(
            actions,
            "POST",
            new Uri("http://127.0.0.1:7700/mode"),
            new MemoryStream(body),
            Encoding.UTF8,
            CancellationToken.None);

        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task Post_mode_unknown_project_returns_404()
    {
        var actions = new FakeActions { Exists = false };
        var body = Encoding.UTF8.GetBytes("""{"projectId":"missing","mode":"ai-controlled"}""");
        var response = await ControlPlaneHttpRouter.DispatchAsync(
            actions,
            "POST",
            new Uri("http://127.0.0.1:7700/mode"),
            new MemoryStream(body),
            Encoding.UTF8,
            CancellationToken.None);

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task Post_app_quit_schedules_quit()
    {
        var actions = new FakeActions();
        var response = await ControlPlaneHttpRouter.DispatchAsync(
            actions,
            "POST",
            new Uri("http://127.0.0.1:7700/app/quit"),
            new MemoryStream(),
            Encoding.UTF8,
            CancellationToken.None);

        Assert.Equal(202, response.StatusCode);
        Assert.True(actions.QuitRequested);
        var json = System.Text.Json.JsonSerializer.Serialize(response.Body);
        Assert.Contains("quitting", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_app_quit_unavailable_returns_503()
    {
        var actions = new FakeActions { QuitAvailable = false };
        var response = await ControlPlaneHttpRouter.DispatchAsync(
            actions,
            "POST",
            new Uri("http://127.0.0.1:7700/app/quit"),
            new MemoryStream(),
            Encoding.UTF8,
            CancellationToken.None);

        Assert.Equal(503, response.StatusCode);
        Assert.False(actions.QuitRequested);
    }

    private sealed class FakeActions : IControlPlaneActions
    {
        public bool ListCalled { get; private set; }
        public bool Exists { get; set; }
        public bool QuitAvailable { get; set; } = true;
        public bool QuitRequested { get; private set; }
        public string? LastBusyProjectId { get; private set; }
        public bool? LastBusySuppress { get; private set; }
        public string? LastRebuildProjectId { get; private set; }
        public string? LastStopProjectId { get; private set; }
        public ProjectBuildControlMode Mode { get; set; } = ProjectBuildControlMode.FileWatching;

        public IReadOnlyList<ControlPlaneProjectInfo> ListProjects()
        {
            ListCalled = true;
            return
            [
                new ControlPlaneProjectInfo("abc", "Demo", @"C:\src\Demo", "Demo.csproj", true)
            ];
        }

        public bool ProjectExists(string projectId) => Exists;

        public bool RequestAppQuit()
        {
            if (!QuitAvailable)
            {
                return false;
            }

            QuitRequested = true;
            return true;
        }

        public ControlPlaneSessionStatus GetSession(string projectId) =>
            new(ControlPlaneSessionState.Idle, DateTimeOffset.UtcNow, false, true);

        public ControlPlaneSessionStatus MarkBusy(string projectId, bool? suppressAutoBuildTests)
        {
            LastBusyProjectId = projectId;
            LastBusySuppress = suppressAutoBuildTests;
            return new ControlPlaneSessionStatus(
                ControlPlaneSessionState.Busy,
                DateTimeOffset.UtcNow,
                true,
                suppressAutoBuildTests ?? true);
        }

        public ControlPlaneSessionStatus MarkIdle(string projectId, bool? suppressAutoBuildTests) =>
            new(ControlPlaneSessionState.Idle, DateTimeOffset.UtcNow, true, suppressAutoBuildTests ?? true);

        public ControlPlaneModeStatus GetBuildControlMode(string projectId) =>
            new(projectId, Mode, ProjectBuildControlModeWire.ToWire(Mode));

        public ControlPlaneModeStatus SetBuildControlMode(string projectId, ProjectBuildControlMode mode)
        {
            var previous = Mode;
            Mode = mode;
            return new ControlPlaneModeStatus(
                projectId,
                mode,
                ProjectBuildControlModeWire.ToWire(mode),
                previous,
                ProjectBuildControlModeWire.ToWire(previous));
        }

        public Task<ControlPlaneRebuildResult> RebuildAsync(
            ControlPlaneRebuildRequest request,
            CancellationToken cancellationToken)
        {
            LastRebuildProjectId = request.ProjectId;
            return Task.FromResult(new ControlPlaneRebuildResult(
                true,
                "Demo.csproj",
                "pass",
                0,
                [],
                null));
        }

        public ControlPlaneWatchStatus GetWatch(string projectId) =>
            new(ControlPlaneWatchState.Stopped, null);

        public ControlPlaneWatchStatus PauseWatch(string projectId) =>
            new(ControlPlaneWatchState.Paused, null);

        public ControlPlaneWatchStatus ResumeWatch(string projectId) =>
            new(ControlPlaneWatchState.Running, 1234);

        public Task<ControlPlaneRunStopResult> StopRunAsync(
            string projectId,
            CancellationToken cancellationToken)
        {
            LastStopProjectId = projectId;
            return Task.FromResult(new ControlPlaneRunStopResult(
                true,
                WasRunning: true,
                ExitCode: 0,
                new ControlPlaneWatchStatus(ControlPlaneWatchState.Paused, null)));
        }

        public Task<ControlPlaneRunTestsResult> RunTestsAsync(
            ControlPlaneRunTestsRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ControlPlaneRunTestsResult(
                true,
                "Demo.csproj",
                new ControlPlaneTestCounts(0, 2, 0),
                [],
                null));

        public Task<ControlPlaneShipCheckResult> ShipCheckAsync(
            ControlPlaneShipCheckRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ControlPlaneShipCheckResult(true, "Demo.csproj", "pass", null, [], null));
    }
}
