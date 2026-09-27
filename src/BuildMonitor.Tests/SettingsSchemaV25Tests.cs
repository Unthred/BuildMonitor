using System.Text.Json;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Tests;

public sealed class SettingsSchemaV25Tests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [Fact]
    public void Serialize_persists_derivedFromProjectId()
    {
        var settings = new AppSettings
        {
            SchemaVersion = SettingsSchemaV25.Version,
            Projects =
            [
                new MonitoredProjectSettings
                {
                    Id = "derived",
                    DisplayName = "App (wt)",
                    Local = new LocalProjectAttachment
                    {
                        RootFolder = @"C:\src\App-wt",
                        ProjectFile = "App.csproj",
                        DerivedFromProjectId = "parent1"
                    }
                }
            ]
        };

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        Assert.Contains("\"schemaVersion\": 25", json, StringComparison.Ordinal);
        Assert.Contains("\"derivedFromProjectId\": \"parent1\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_omits_null_derivedFromProjectId()
    {
        var settings = new AppSettings
        {
            SchemaVersion = SettingsSchemaV25.Version,
            Projects =
            [
                new MonitoredProjectSettings
                {
                    Id = "main",
                    DisplayName = "App",
                    Local = new LocalProjectAttachment
                    {
                        RootFolder = @"C:\src\App",
                        ProjectFile = "App.csproj",
                        DerivedFromProjectId = null
                    }
                }
            ]
        };

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        Assert.DoesNotContain("derivedFromProjectId", json, StringComparison.OrdinalIgnoreCase);
    }
}
