using BuildMonitor.Core.Abstractions;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Infrastructure.AzureDevOps;

/// <summary>
/// Draft Azure connection editor for Settings (v1: single connection).
/// Metadata and PAT stay draft until <see cref="TryCommitAfterValidationAsync"/> succeeds.
/// Test connection may use a pending PAT without persisting it.
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

    public bool HasPendingPat => !string.IsNullOrWhiteSpace(pendingPat);

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

    /// <summary>Stores a PAT in memory only until a successful validated commit.</summary>
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
    /// Applies draft connection metadata for validation, runs <paramref name="validate"/>,
    /// and only then persists a pending PAT. On validation failure, restores prior Connections
    /// and leaves the secret store unchanged.
    /// </summary>
    public async Task<AzureConnectionCommitResult> TryCommitAfterValidationAsync(
        Func<AppSettings, IReadOnlyList<string>> validate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validate);

        if (!TryBuildConnectionsForSave(out var nextConnections, out var buildError))
        {
            return AzureConnectionCommitResult.Failed([buildError!]);
        }

        var previousConnections = CloneConnections(settings.Connections);
        settings.Connections = nextConnections;

        var errors = validate(settings);
        if (errors.Count > 0)
        {
            settings.Connections = previousConnections;
            return AzureConnectionCommitResult.Failed(errors);
        }

        if (!string.IsNullOrWhiteSpace(pendingPat))
        {
            await secretStore.SaveAsync(draftConnectionId, pendingPat, cancellationToken);
            credentialStored = true;
            pendingPat = null;
        }

        return AzureConnectionCommitResult.Ok();
    }

    internal bool TryBuildConnectionsForSave(
        out List<AzureDevOpsConnectionSettings> connections,
        out string? error)
    {
        connections = [];
        error = null;

        if (string.IsNullOrWhiteSpace(draftOrganizationUrl)
            && string.IsNullOrWhiteSpace(draftDisplayName)
            && !credentialStored
            && pendingPat is null)
        {
            return true;
        }

        if (!AzureOrganizationUrl.TryNormalize(draftOrganizationUrl, out var normalized, out var urlError))
        {
            error = urlError;
            return false;
        }

        var displayName = string.IsNullOrWhiteSpace(draftDisplayName)
            ? DeriveDisplayName(normalized)
            : draftDisplayName.Trim();

        connections =
        [
            new AzureDevOpsConnectionSettings
            {
                Id = draftConnectionId,
                DisplayName = displayName,
                OrganizationUrl = normalized
            }
        ];
        return true;
    }

    private static List<AzureDevOpsConnectionSettings> CloneConnections(
        IEnumerable<AzureDevOpsConnectionSettings> source) =>
        source.Select(c => new AzureDevOpsConnectionSettings
        {
            Id = c.Id,
            DisplayName = c.DisplayName,
            OrganizationUrl = c.OrganizationUrl
        }).ToList();

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

public sealed class AzureConnectionCommitResult
{
    private AzureConnectionCommitResult(bool succeeded, IReadOnlyList<string> errors)
    {
        Succeeded = succeeded;
        Errors = errors;
    }

    public bool Succeeded { get; }

    public IReadOnlyList<string> Errors { get; }

    public static AzureConnectionCommitResult Ok() => new(true, []);

    public static AzureConnectionCommitResult Failed(IReadOnlyList<string> errors) =>
        new(false, errors);
}
