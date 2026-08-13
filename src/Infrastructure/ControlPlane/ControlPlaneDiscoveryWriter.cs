using System.Text.Json;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.ControlPlane;

/// <summary>
/// Writes %LocalAppData%/BuildMonitor/control-plane.json so Cursor agents can discover the loopback API.
/// </summary>
public static class ControlPlaneDiscoveryWriter
{
    public const string FileName = "control-plane.json";
    public const int DefaultPort = 7700;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string GetPath(string appDataDirectory) =>
        Path.Combine(appDataDirectory, FileName);

    public static void Write(
        string appDataDirectory,
        bool enabled,
        int? boundPort,
        IReadOnlyList<ControlPlaneProjectInfo> projects)
    {
        Directory.CreateDirectory(appDataDirectory);
        var port = boundPort ?? DefaultPort;
        var dto = new DiscoveryDto(
            SchemaVersion: 1,
            Enabled: enabled && boundPort is not null,
            Port: port,
            BaseUrl: $"http://127.0.0.1:{port}/",
            UpdatedAtUtc: DateTimeOffset.UtcNow.ToString("O"),
            Projects: projects
                .Select(p => new DiscoveryProjectDto(
                    p.Id,
                    p.DisplayName,
                    p.RootFolder,
                    p.ProjectFile,
                    p.IsActiveInSession))
                .ToList());

        var path = GetPath(appDataDirectory);
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static void WriteDisabled(string appDataDirectory)
    {
        Write(appDataDirectory, enabled: false, boundPort: null, projects: []);
    }

    private sealed record DiscoveryDto(
        int SchemaVersion,
        bool Enabled,
        int Port,
        string BaseUrl,
        string UpdatedAtUtc,
        IReadOnlyList<DiscoveryProjectDto> Projects);

    private sealed record DiscoveryProjectDto(
        string Id,
        string DisplayName,
        string RootFolder,
        string ProjectFile,
        bool IsActiveInSession);
}
