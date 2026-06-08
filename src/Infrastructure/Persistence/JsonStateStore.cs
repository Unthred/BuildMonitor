using System.Text.Json;
using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.Persistence;

public sealed class JsonStateStore(string stateFilePath) : IStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<MonitorSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(stateFilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(stateFilePath);
        return await JsonSerializer.DeserializeAsync<MonitorSnapshot>(stream, JsonOptions, cancellationToken);
    }

    public async Task SaveAsync(MonitorSnapshot snapshot, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(stateFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(stateFilePath);
        await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
    }
}
