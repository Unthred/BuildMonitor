using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Infrastructure.AzureDevOps;

/// <summary>
/// Draft Azure connection editor for Settings (v1: single connection).
/// Persists connection metadata into <see cref="AppSettings.Connections"/> only on commit;
/// PAT is written to <see cref="IAzureConnectionSecretStore"/> only when the user provides a new value.
/// </summary>
public sealed class AzureConnectionSettingsEditor(
    AppSettings settings,
    IAzureConnectionSecretStore secretStore,
    IAzureDevOpsDiscoveryClient discoveryClient)
{
    private string draftConnectionId = Guid.NewGuid().ToString("N");
    private string draftDisplayName = string.Empty;
    private string draftOrganizationUrl = string.Empty;
    private bool credentialStored;
    private string? pendingPat;

    public string DraftDisplayName
    {
        get => draftDisplayName;
        set => draftDisplayName = value ?? string.Empty;
    }

    public string DraftOrganizationUrl
    {
        get => draftOrganizationUrl;
        set => draftOrganizationUrl = value ?? string.Empty;
    }

    public string CredentialStatusText =>
        credentialStored ? "Credential stored" : "No credential stored";

    public bool HasStoredCredential => credentialStored;

    public string ConnectionId => draftConnectionId;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var existing = settings.Connections.FirstOrDefault();
        if (existing is null)
        {
            draftConnectionId = Guid.NewGuid().ToString("N");
            draftDisplayName = string.Empty;
            draftOrganizationUrl = string.Empty;
            credentialStored = false;
            pendingPat = null;
            return;
        }

        draftConnectionId = existing.Id;
        draftDisplayName = existing.DisplayName;
        draftOrganizationUrl = existing.OrganizationUrl;
        credentialStored = await secretStore.ExistsAsync(draftConnectionId, cancellationToken);
        pendingPat = null;
    }

    /// <summary>Stores a PAT in memory only until <see cref="CommitToSettingsAsync"/>.</summary>
    public void SetPendingPat(string? pat)
    {
        pendingPat = string.IsNullOrWhiteSpace(pat) ? null : pat.Trim();
    }

    public void ClearPendingPat() => pendingPat = null;

    public async Task<AzureConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        if (!AzureOrganizationUrl.TryNormalize(draftOrganizationUrl, out var normalized, out var urlError))
        {
            return new AzureConnectionTestResult(AzureConnectionTestOutcome.OrganizationUnreachable, urlError);
        }

        var pat = pendingPat;
        if (string.IsNullOrWhiteSpace(pat))
        {
            pat = await secretStore.LoadAsync(draftConnectionId, cancellationToken);
        }

        var connection = new AzureDevOpsConnectionSettings
        {
            Id = draftConnectionId,
            DisplayName = draftDisplayName,
            OrganizationUrl = normalized
        };

        return await discoveryClient.TestConnectionAsync(connection, pat, cancellationToken);
    }

    /// <summary>
    /// Writes connection metadata into settings and optional pending PAT to the secret store.
    /// Does not delete an existing PAT when the password box is empty.
    /// </summary>
    public async Task CommitToSettingsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(draftOrganizationUrl) && string.IsNullOrWhiteSpace(draftDisplayName) && !credentialStored && pendingPat is null)
        {
            settings.Connections = [];
            return;
        }

        if (!AzureOrganizationUrl.TryNormalize(draftOrganizationUrl, out var normalized, out var error))
        {
            throw new InvalidOperationException(error);
        }

        if (string.IsNullOrWhiteSpace(draftDisplayName))
        {
            draftDisplayName = DeriveDisplayName(normalized);
        }

        settings.Connections =
        [
            new AzureDevOpsConnectionSettings
            {
                Id = draftConnectionId,
                DisplayName = draftDisplayName.Trim(),
                OrganizationUrl = normalized
            }
        ];

        if (!string.IsNullOrWhiteSpace(pendingPat))
        {
            await secretStore.SaveAsync(draftConnectionId, pendingPat, cancellationToken);
            credentialStored = true;
            pendingPat = null;
        }
    }

    private static string DeriveDisplayName(string organizationUrl)
    {
        if (Uri.TryCreate(organizationUrl, UriKind.Absolute, out var uri))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
            {
                return segments[0];
            }

            return uri.Host;
        }

        return "Azure DevOps";
    }
}
