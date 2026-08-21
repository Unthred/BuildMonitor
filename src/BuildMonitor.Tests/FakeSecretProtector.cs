using System.Text;
using BuildMonitor.Infrastructure.Security;

namespace BuildMonitor.Tests;

internal sealed class FakeSecretProtector : ISecretProtector
{
    public byte[] Protect(byte[] plaintext) =>
        Encoding.UTF8.GetBytes("P:" + Convert.ToBase64String(plaintext));

    public byte[] Unprotect(byte[] protectedBytes)
    {
        var text = Encoding.UTF8.GetString(protectedBytes);
        if (!text.StartsWith("P:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Not a fake-protected payload.");
        }

        return Convert.FromBase64String(text[2..]);
    }
}
