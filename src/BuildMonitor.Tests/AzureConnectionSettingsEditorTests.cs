using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.AzureDevOps;
using BuildMonitor.Infrastructure.Security;

namespace BuildMonitor.Tests;

public sealed class AzureConnectionSettingsEditorTests
{
    [Fact]
    public async Task Validated_commit_persists_connection_without_pat_in_settings_and_keeps_id_stable()
    {
        var settings = new AppSettings();
        var dir = Path.Combine(Path.GetTempPath(), "bm-ed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new AzureConnectionSecretStore(dir, new FakeSecretProtector());
            var editor = new AzureConnectionSettingsEditor(settings, store, new StubDiscovery());
            await editor.LoadAsync(CancellationToken.None);
            var id = editor.ConnectionId;

            editor.DraftDisplayName = "Contoso";
            editor.DraftOrganizationUrl = "https://dev.azure.com/contoso/";
            editor.SetPendingPat("super-secret-pat");
            var result = await editor.TryCommitAfterValidationAsync(_ => [], CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Single(settings.Connections);
            Assert.Equal(id, settings.Connections[0].Id);
            Assert.Equal("https://dev.azure.com/contoso", settings.Connections[0].OrganizationUrl);
            Assert.Equal("super-secret-pat", await store.LoadAsync(id, CancellationToken.None));

            var json = System.Text.Json.JsonSerializer.Serialize(settings);
            Assert.DoesNotContain("super-secret-pat", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"pat\"", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Test_uses_pending_pat_without_overwriting_stored_secret()
    {
        var settings = new AppSettings
        {
            Connections =
            [
                new AzureDevOpsConnectionSettings
                {
                    Id = "fixedid",
                    DisplayName = "Contoso",
                    OrganizationUrl = "https://dev.azure.com/contoso"
                }
            ]
        };
        var dir = Path.Combine(Path.GetTempPath(), "bm-ed2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new AzureConnectionSecretStore(dir, new FakeSecretProtector());
            await store.SaveAsync("fixedid", "stored-pat", CancellationToken.None);
            var stub = new StubDiscovery();
            var editor = new AzureConnectionSettingsEditor(settings, store, stub);
            await editor.LoadAsync(CancellationToken.None);
            editor.SetPendingPat("draft-pat");

            var result = await editor.TestConnectionAsync(CancellationToken.None);
            Assert.Equal(AzureConnectionTestOutcome.Success, result.Outcome);
            Assert.Equal("draft-pat", stub.LastPat);
            Assert.Equal("stored-pat", await store.LoadAsync("fixedid", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Rejected_save_keeps_original_pat_and_does_not_persist_pending_replacement()
    {
        var settings = new AppSettings
        {
            Connections =
            [
                new AzureDevOpsConnectionSettings
                {
                    Id = "fixedid",
                    DisplayName = "Contoso",
                    OrganizationUrl = "https://dev.azure.com/contoso"
                }
            ],
            Projects =
            [
                new MonitoredProjectSettings
                {
                    Id = "p1",
                    DisplayName = "Local",
                    IsActiveInSession = true,
                    Local = new LocalProjectAttachment
                    {
                        RootFolder = @"C:\src\App",
                        ProjectFile = "App.csproj"
                    }
                }
            ]
        };
        var dir = Path.Combine(Path.GetTempPath(), "bm-ed3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new AzureConnectionSecretStore(dir, new FakeSecretProtector());
            await store.SaveAsync("fixedid", "original-pat", CancellationToken.None);
            var editor = new AzureConnectionSettingsEditor(settings, store, new StubDiscovery());
            await editor.LoadAsync(CancellationToken.None);

            editor.DraftDisplayName = "Contoso Renamed";
            editor.DraftOrganizationUrl = "https://dev.azure.com/contoso-new";
            editor.SetPendingPat("replacement-pat-should-not-persist");

            var result = await editor.TryCommitAfterValidationAsync(
                _ => ["Unrelated project setting is invalid."],
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("Unrelated project setting is invalid.", result.Errors);
            Assert.Equal("Contoso", settings.Connections[0].DisplayName);
            Assert.Equal("https://dev.azure.com/contoso", settings.Connections[0].OrganizationUrl);
            Assert.Equal("original-pat", await store.LoadAsync("fixedid", CancellationToken.None));
            Assert.True(editor.HasPendingPat);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void OrganizationUrl_rejects_non_https()
    {
        Assert.False(AzureOrganizationUrl.TryNormalize("http://dev.azure.com/x", out _, out var error));
        Assert.Contains("https", error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubDiscovery : BuildMonitor.Core.Abstractions.IAzureDevOpsDiscoveryClient
    {
        public string? LastPat { get; private set; }

        public Task<AzureConnectionTestResult> TestConnectionAsync(
            AzureDevOpsConnectionSettings connection,
            string? pat,
            CancellationToken cancellationToken)
        {
            LastPat = pat;
            return Task.FromResult(new AzureConnectionTestResult(AzureConnectionTestOutcome.Success, "ok"));
        }

        public Task<IReadOnlyList<AzureProjectSummary>> ListProjectsAsync(
            AzureDevOpsConnectionSettings connection,
            string pat,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AzureProjectSummary>>([]);

        public Task<IReadOnlyList<AzureRepositorySummary>> ListRepositoriesAsync(
            AzureDevOpsConnectionSettings connection,
            string pat,
            string projectIdOrName,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AzureRepositorySummary>>([]);

        public Task<IReadOnlyList<AzurePipelineSummary>> ListPipelinesForRepositoryAsync(
            AzureDevOpsConnectionSettings connection,
            string pat,
            string projectIdOrName,
            string repositoryId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AzurePipelineSummary>>([]);
    }
}
