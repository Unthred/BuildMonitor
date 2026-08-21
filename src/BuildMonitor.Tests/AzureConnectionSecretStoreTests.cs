using BuildMonitor.Infrastructure.Security;

namespace BuildMonitor.Tests;

public sealed class AzureConnectionSecretStoreTests
{
    [Fact]
    public async Task Save_load_overwrite_delete_round_trip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bm-secrets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new AzureConnectionSecretStore(dir, new FakeSecretProtector());
            const string id = "conn1";

            Assert.False(await store.ExistsAsync(id, CancellationToken.None));
            Assert.Null(await store.LoadAsync(id, CancellationToken.None));

            await store.SaveAsync(id, "pat-one", CancellationToken.None);
            Assert.True(await store.ExistsAsync(id, CancellationToken.None));
            Assert.Equal("pat-one", await store.LoadAsync(id, CancellationToken.None));

            await store.SaveAsync(id, "pat-two", CancellationToken.None);
            Assert.Equal("pat-two", await store.LoadAsync(id, CancellationToken.None));

            var files = Directory.GetFiles(dir, "ado-*.dpapi");
            Assert.Single(files);
            var disk = await File.ReadAllTextAsync(files[0]);
            Assert.DoesNotContain("pat-two", disk, StringComparison.Ordinal);

            await store.DeleteAsync(id, CancellationToken.None);
            Assert.False(await store.ExistsAsync(id, CancellationToken.None));
            Assert.Null(await store.LoadAsync(id, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
