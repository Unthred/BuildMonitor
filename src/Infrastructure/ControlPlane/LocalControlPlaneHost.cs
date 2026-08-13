using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildMonitor.Infrastructure.ControlPlane;

/// <summary>HttpListener loopback host for the agent control plane.</summary>
public sealed class LocalControlPlaneHost : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IControlPlaneActions actions;
    private readonly ControlPlaneMetricsStore? metrics;
    private readonly object sync = new();
    private HttpListener? listener;
    private CancellationTokenSource? loopCts;
    private Task? loopTask;
    private int port;
    private bool enabled;

    public LocalControlPlaneHost(IControlPlaneActions actions, ControlPlaneMetricsStore? metrics = null)
    {
        this.actions = actions;
        this.metrics = metrics;
    }

    public bool IsRunning
    {
        get
        {
            lock (sync)
            {
                return listener?.IsListening == true;
            }
        }
    }

    public int? BoundPort
    {
        get
        {
            lock (sync)
            {
                return listener?.IsListening == true ? port : null;
            }
        }
    }

    public void ApplySettings(bool enabled, int port)
    {
        port = Math.Clamp(port, 1024, 65535);
        lock (sync)
        {
            if (this.enabled == enabled && this.port == port && listener?.IsListening == true)
            {
                return;
            }

            this.enabled = enabled;
            this.port = port;
        }

        Stop();
        if (enabled)
        {
            Start(port);
        }
    }

    private void Start(int listenPort)
    {
        var prefix = $"http://127.0.0.1:{listenPort}/";
        var http = new HttpListener();
        http.Prefixes.Add(prefix);
        try
        {
            http.Start();
        }
        catch (HttpListenerException)
        {
            http.Close();
            throw;
        }

        var cts = new CancellationTokenSource();
        lock (sync)
        {
            listener = http;
            loopCts = cts;
            port = listenPort;
            loopTask = Task.Run(() => ListenLoopAsync(http, cts.Token));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        HttpListener? http;
        Task? task;
        lock (sync)
        {
            cts = loopCts;
            http = listener;
            task = loopTask;
            loopCts = null;
            listener = null;
            loopTask = null;
        }

        try
        {
            cts?.Cancel();
        }
        catch
        {
            // ignore
        }

        try
        {
            if (http?.IsListening == true)
            {
                http.Stop();
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            http?.Close();
        }
        catch
        {
            // ignore
        }

        cts?.Dispose();
        try
        {
            task?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore
        }
    }

    private async Task ListenLoopAsync(HttpListener http, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && http.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await http.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }

            _ = Task.Run(() => HandleRequestAsync(context, cancellationToken), CancellationToken.None);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var response = await ControlPlaneHttpRouter.DispatchAsync(
                actions,
                context.Request.HttpMethod,
                context.Request.Url,
                context.Request.InputStream,
                context.Request.ContentEncoding,
                cancellationToken,
                metrics).ConfigureAwait(false);

            await WriteJsonAsync(context.Response, response.StatusCode, response.Body).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var body = new { error = ex.Message };
            await WriteJsonAsync(context.Response, 500, body).ConfigureAwait(false);
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object body)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.OutputStream.Close();
    }

    public void Dispose() => Stop();
}
