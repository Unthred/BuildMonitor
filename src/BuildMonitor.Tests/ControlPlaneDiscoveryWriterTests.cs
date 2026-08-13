using BuildMonitor.Core.Models;
using BuildMonitor.Infrastructure.ControlPlane;

namespace BuildMonitor.Tests;

public sealed class ControlPlaneDiscoveryWriterTests
{
    [Fact]
    public void Write_creates_json_with_base_url_and_projects()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bm-cp-" + Guid.NewGuid().ToString("N"));
        try
        {
            ControlPlaneDiscoveryWriter.Write(
                dir,
                enabled: true,
                boundPort: 7700,
                projects:
                [
                    new ControlPlaneProjectInfo(
                        "abc",
                        "Demo",
                        @"C:\src\Demo",
                        "Demo.csproj",
                        true)
                ]);

            var path = ControlPlaneDiscoveryWriter.GetPath(dir);
            Assert.True(File.Exists(path));
            var json = File.ReadAllText(path);
            Assert.Contains("http://127.0.0.1:7700/", json, StringComparison.Ordinal);
            Assert.Contains("\"id\": \"abc\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"enabled\": true", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
