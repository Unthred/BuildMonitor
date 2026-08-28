using System.Text.Json;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Tests;

public sealed class SettingsSchemaV22Tests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [Fact]
    public void Load_v21_json_leaves_linkBrowserRegisteredId_null()
    {
        const string json = """
            {
              "schemaVersion": 21,
              "projects": [
                {
                  "id": "abc",
                  "displayName": "Legacy",
                  "local": {
                    "rootFolder": "C:\\src\\App",
                    "projectFile": "App.csproj"
                  }
                }
              ]
            }
            """;

        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)!;
        if (settings.SchemaVersion < SettingsSchemaV22.Version)
        {
            settings.SchemaVersion = SettingsSchemaV22.Version;
        }

        Assert.Equal(SettingsSchemaV22.Version, settings.SchemaVersion);
        var project = Assert.Single(settings.Projects);
        Assert.Null(project.LinkBrowserRegisteredId);

        var saved = JsonSerializer.Serialize(settings, JsonOptions);
        Assert.Contains("\"schemaVersion\": 22", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("linkBrowserRegisteredId", saved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_omits_null_linkBrowserRegisteredId()
    {
        var settings = new AppSettings
        {
            SchemaVersion = SettingsSchemaV22.Version,
            Projects =
            [
                new MonitoredProjectSettings
                {
                    Id = "p1",
                    DisplayName = "P1",
                    LinkBrowserRegisteredId = null
                }
            ]
        };

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        Assert.DoesNotContain("linkBrowserRegisteredId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_persists_explicit_linkBrowserRegisteredId()
    {
        var settings = new AppSettings
        {
            SchemaVersion = SettingsSchemaV22.Version,
            Projects =
            [
                new MonitoredProjectSettings
                {
                    Id = "p1",
                    DisplayName = "P1",
                    LinkBrowserRegisteredId = "MSEdgeHTM"
                }
            ]
        };

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        Assert.Contains("\"linkBrowserRegisteredId\": \"MSEdgeHTM\"", json, StringComparison.Ordinal);
    }
}
