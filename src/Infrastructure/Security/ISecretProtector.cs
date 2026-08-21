namespace BuildMonitor.Infrastructure.Security;

/// <summary>Protects secret bytes at rest (production uses CurrentUser DPAPI).</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);

    byte[] Unprotect(byte[] protectedBytes);
}
