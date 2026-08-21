using System.Text;
using BuildMonitor.Core.Abstractions;

namespace BuildMonitor.Infrastructure.Security;

/// <summary>
/// Stores Azure DevOps PATs under a secrets directory as <c>ado-{connectionId}.dpapi</c>.
/// Never logs PAT values.
/// </summary>
public sealed class AzureConnectionSecretStore(
    string secretsDirectory,
    ISecretProtector protector) : IAzureConnectionSecretStore
{
    public static string DefaultSecretsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BuildMonitor",
            "secrets");

    public async Task SaveAsync(string connectionId, string pat, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pat);

        Directory.CreateDirectory(secretsDirectory);
        var path = GetPath(connectionId);
        var protectedBytes = protector.Protect(Encoding.UTF8.GetBytes(pat));
        var tempPath = path + ".tmp";
        await File.WriteAllBytesAsync(tempPath, protectedBytes, cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }

    public async Task<string?> LoadAsync(string connectionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        var path = GetPath(connectionId);
        if (!File.Exists(path))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var raw = protector.Unprotect(protectedBytes);
        return Encoding.UTF8.GetString(raw);
    }

    public Task DeleteAsync(string connectionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        var path = GetPath(connectionId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string connectionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        return Task.FromResult(File.Exists(GetPath(connectionId)));
    }

    private string GetPath(string connectionId)
    {
        var safeId = SanitizeConnectionId(connectionId);
        return Path.Combine(secretsDirectory, $"ado-{safeId}.dpapi");
    }

    private static string SanitizeConnectionId(string connectionId)
    {
        Span<char> buffer = stackalloc char[connectionId.Length];
        var n = 0;
        foreach (var c in connectionId)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
            {
                buffer[n++] = c;
            }
        }

        if (n == 0)
        {
            throw new ArgumentException("Connection id must contain alphanumeric characters.", nameof(connectionId));
        }

        return new string(buffer[..n]);
    }
}
