using System.Text.Json;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Tests;

public sealed class SettingsSchemaV24Tests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [Fact]
    public void Load_v23_json_defaults_applicationUrl_empty()
    {
        const string json = """
            {
              "schemaVersion": 23,
              "projects": [
                {
                  "id": "abc",
                  "displayName": "App",
                  "local": {
                    "rootFolder": "C:\\src\\App",
                    "projectFile": "App.csproj",
                    "launchProfile": "https"
                  }
                }
              ]
            }
            """;

        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)!;
        settings.SchemaVersion = SettingsSchemaV24.Version;

        Assert.Equal(string.Empty, settings.Projects[0].Local!.ApplicationUrl);
    }

    [Fact]
    public void Serialize_persists_applicationUrl()
    {
        var settings = new AppSettings
        {
            SchemaVersion = SettingsSchemaV24.Version,
            Projects =
            [
                new MonitoredProjectSettings
                {
                    Id = "worktree",
                    DisplayName = "2ndWitherbyConnect (main)",
                    Local = new LocalProjectAttachment
                    {
                        RootFolder = @"C:\src\App-worktree",
                        ProjectFile = "WitherbyConnect.csproj",
                        ApplicationUrl = "https://localhost:44349"
                    }
                }
            ]
        };

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        Assert.Contains("\"schemaVersion\": 24", json, StringComparison.Ordinal);
        Assert.Contains("\"applicationUrl\": \"https://localhost:44349\"", json, StringComparison.Ordinal);
    }
}
