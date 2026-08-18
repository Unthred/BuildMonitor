using System.Text;
using System.Text.Json;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.ControlPlane;

internal sealed record ControlPlaneHttpResponse(int StatusCode, object Body, string? ProjectId = null);

internal static class ControlPlaneHttpRouter
{
    public static async Task<ControlPlaneHttpResponse> DispatchAsync(
        IControlPlaneActions actions,
        string method,
        Uri? url,
        Stream bodyStream,
        Encoding encoding,
        CancellationToken cancellationToken,
        ControlPlaneMetricsStore? metrics = null)
    {
        method = method.ToUpperInvariant();
        var path = (url?.AbsolutePath ?? "/").TrimEnd('/');
        if (path.Length == 0)
        {
            path = "/";
        }

        var routeKey = path.TrimStart('/');
        string? projectIdForMetrics = null;
        ControlPlaneHttpResponse response;
        try
        {
            response = await DispatchCoreAsync(
                actions,
                method,
                path,
                url,
                bodyStream,
                encoding,
                cancellationToken).ConfigureAwait(false);
            projectIdForMetrics = response.ProjectId;
        }
        catch
        {
            metrics?.RecordHttp(null, routeKey, 500);
            throw;
        }

        metrics?.RecordHttp(projectIdForMetrics ?? ExtractProjectIdFromQuery(url), routeKey, response.StatusCode);
        return response;
    }

    private static async Task<ControlPlaneHttpResponse> DispatchCoreAsync(
        IControlPlaneActions actions,
        string method,
        string path,
        Uri? url,
        Stream bodyStream,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        if (method == "GET" && path == "/projects")
        {
            return Ok(actions.ListProjects());
        }

        if (method == "GET" && path == "/session")
        {
            if (!TryGetProjectId(url, body: null, out var projectId, out var error))
            {
                return BadRequest(error!);
            }

            if (!actions.ProjectExists(projectId!))
            {
                return NotFound(projectId!);
            }

            var session = actions.GetSession(projectId!);
            return Ok(SessionJson(session), projectId);
        }

        if (method == "POST" && path is "/session/busy" or "/session/idle")
        {
            var payload = await ReadBodyAsync(bodyStream, encoding, cancellationToken).ConfigureAwait(false);
            if (!TryGetProjectId(url, payload, out var projectId, out var error))
            {
                return BadRequest(error!);
            }

            if (!actions.ProjectExists(projectId!))
            {
                return NotFound(projectId!);
            }

            bool? suppress = null;
            if (payload?.TryGetProperty("suppressAutoBuildTests", out var suppressEl) == true
                && suppressEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                suppress = suppressEl.GetBoolean();
            }

            var session = path == "/session/busy"
                ? actions.MarkBusy(projectId!, suppress)
                : actions.MarkIdle(projectId!, suppress);

            return Ok(SessionJson(session), projectId);
        }

        if (method == "GET" && path == "/watch")
        {
            if (!TryGetProjectId(url, body: null, out var projectId, out var error))
            {
                return BadRequest(error!);
            }

            if (!actions.ProjectExists(projectId!))
            {
                return NotFound(projectId!);
            }

            return Ok(WatchJson(actions.GetWatch(projectId!)), projectId);
        }

        if (method == "POST" && path is "/watch/pause" or "/watch/resume")
        {
            var payload = await ReadBodyAsync(bodyStream, encoding, cancellationToken).ConfigureAwait(false);
            if (!TryGetProjectId(url, payload, out var projectId, out var error))
            {
                return BadRequest(error!);
            }

            if (!actions.ProjectExists(projectId!))
            {
                return NotFound(projectId!);
            }

            var watch = path == "/watch/pause"
                ? actions.PauseWatch(projectId!)
                : actions.ResumeWatch(projectId!);

            return Ok(WatchJson(watch), projectId);
        }

        if (method == "POST" && path == "/run/rebuild")
        {
            var payload = await ReadBodyAsync(bodyStream, encoding, cancellationToken).ConfigureAwait(false);
            if (!TryGetProjectId(url, payload, out var projectId, out var error))
            {
                return BadRequest(error!);
            }

            if (!actions.ProjectExists(projectId!))
            {
                return NotFound(projectId!);
            }

            string? configuration = null;
            if (payload is not null
                && payload.Value.TryGetProperty("configuration", out var cfg)
                && cfg.ValueKind == JsonValueKind.String)
            {
                configuration = cfg.GetString();
            }

            try
            {
                var result = await actions.RebuildAsync(
                    new ControlPlaneRebuildRequest(projectId!, configuration),
                    cancellationToken).ConfigureAwait(false);
                return Ok(ToRebuildJson(result), projectId);
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("already running", StringComparison.OrdinalIgnoreCase))
            {
                return new ControlPlaneHttpResponse(409, new { error = ex.Message }, projectId);
            }
        }

        if (method == "POST" && path == "/run/ship-check")
        {
            var payload = await ReadBodyAsync(bodyStream, encoding, cancellationToken).ConfigureAwait(false);
            if (!TryGetProjectId(url, payload, out var projectId, out var error))
            {
                return BadRequest(error!);
            }

            if (!actions.ProjectExists(projectId!))
            {
                return NotFound(projectId!);
            }

            string? configuration = null;
            string? filter = null;
            bool? suppress = null;
            if (payload is not null)
            {
                if (payload.Value.TryGetProperty("configuration", out var cfg)
                    && cfg.ValueKind == JsonValueKind.String)
                {
                    configuration = cfg.GetString();
                }

                if (payload.Value.TryGetProperty("filter", out var filterEl)
                    && filterEl.ValueKind == JsonValueKind.String)
                {
                    filter = filterEl.GetString();
                }

                if (payload.Value.TryGetProperty("suppressAutoBuildTests", out var suppressEl)
                    && suppressEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    suppress = suppressEl.GetBoolean();
                }
            }

            try
            {
                var result = await actions.ShipCheckAsync(
                    new ControlPlaneShipCheckRequest(projectId!, configuration, filter, suppress),
                    cancellationToken).ConfigureAwait(false);
                return Ok(ToShipCheckJson(result), projectId);
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("already running", StringComparison.OrdinalIgnoreCase))
            {
                return new ControlPlaneHttpResponse(409, new { error = ex.Message }, projectId);
            }
        }

        return new ControlPlaneHttpResponse(404, new { error = $"Unknown route {method} {path}" });
    }

    private static object SessionJson(ControlPlaneSessionStatus session) => new
    {
        state = session.State.ToString().ToLowerInvariant(),
        since = session.Since.ToString("O"),
        sessionApiUsed = session.SessionApiUsed,
        suppressAutoBuildTests = session.SuppressAutoBuildTests
    };

    private static object WatchJson(ControlPlaneWatchStatus watch) => new
    {
        watch = watch.Watch.ToString().ToLowerInvariant(),
        pid = watch.Pid
    };

    private static object ToRebuildJson(ControlPlaneRebuildResult result) => new
    {
        ok = result.Ok,
        project = result.Project,
        build = result.Build,
        exitCode = result.ExitCode,
        failures = result.Failures,
        log = result.Log
    };

    private static object ToShipCheckJson(ControlPlaneShipCheckResult result)
    {
        if (result.Tests is null)
        {
            return new
            {
                ok = result.Ok,
                project = result.Project,
                build = result.Build,
                failures = result.Failures,
                log = result.Log
            };
        }

        return new
        {
            ok = result.Ok,
            project = result.Project,
            build = result.Build,
            tests = new
            {
                failed = result.Tests.Failed,
                passed = result.Tests.Passed,
                skipped = result.Tests.Skipped
            },
            failures = result.Failures,
            log = result.Log
        };
    }

    private static bool TryGetProjectId(Uri? url, JsonElement? body, out string? projectId, out string? error)
    {
        projectId = null;
        error = null;

        if (body is { } el
            && el.TryGetProperty("projectId", out var bodyId)
            && bodyId.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(bodyId.GetString()))
        {
            projectId = bodyId.GetString()!.Trim();
            return true;
        }

        if (url is not null)
        {
            foreach (var part in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = part.IndexOf('=');
                if (idx <= 0)
                {
                    continue;
                }

                var key = Uri.UnescapeDataString(part[..idx]);
                if (!key.Equals("projectId", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = Uri.UnescapeDataString(part[(idx + 1)..]);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    projectId = value.Trim();
                    return true;
                }
            }
        }

        error = "projectId is required (query string or JSON body).";
        return false;
    }

    private static async Task<JsonElement?> ReadBodyAsync(
        Stream bodyStream,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            bodyStream,
            encoding,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static string? ExtractProjectIdFromQuery(Uri? url)
    {
        if (url is null || !TryGetProjectId(url, body: null, out var projectId, out _))
        {
            return null;
        }

        return projectId;
    }

    private static ControlPlaneHttpResponse Ok(object body, string? projectId = null) =>
        new(200, body, projectId);

    private static ControlPlaneHttpResponse BadRequest(string error) =>
        new(400, new { error });

    private static ControlPlaneHttpResponse NotFound(string projectId) =>
        new(404, new { error = $"Unknown projectId '{projectId}'." }, projectId);
}
