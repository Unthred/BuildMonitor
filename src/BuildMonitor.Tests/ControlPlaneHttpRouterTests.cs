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

    private sealed class FakeActions : IControlPlaneActions
    {
        public bool ListCalled { get; private set; }
        public bool Exists { get; set; }
        public string? LastBusyProjectId { get; private set; }
        public bool? LastBusySuppress { get; private set; }
        public string? LastRebuildProjectId { get; private set; }

        public IReadOnlyList<ControlPlaneProjectInfo> ListProjects()
        {
            ListCalled = true;
            return
            [
                new ControlPlaneProjectInfo("abc", "Demo", @"C:\src\Demo", "Demo.csproj", true)
            ];
        }

        public bool ProjectExists(string projectId) => Exists;

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

        public Task<ControlPlaneShipCheckResult> ShipCheckAsync(
            ControlPlaneShipCheckRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ControlPlaneShipCheckResult(true, "Demo.csproj", "pass", null, [], null));
    }
}
