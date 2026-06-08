using System.Security.Cryptography;
using System.Text;

namespace BuildMonitor.Infrastructure.Security;

public sealed class PatSecretStore(string patFilePath)
{
    public async Task SaveAsync(string pat, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(patFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(pat),
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);

        await File.WriteAllBytesAsync(patFilePath, protectedBytes, cancellationToken);
    }

    public async Task<string?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(patFilePath))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(patFilePath, cancellationToken);
        var raw = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(raw);
    }
}
