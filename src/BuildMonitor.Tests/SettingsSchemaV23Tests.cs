using System.Text.Json;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Tests;

public sealed class SettingsSchemaV23Tests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [Fact]
    public void Load_v22_json_defaults_both_status_visibility_settings_on()
    {
        const string json = """
            {
              "schemaVersion": 22,
              "projects": [],
              "appBehavior": {
                "followStatusPanelToVirtualDesktop": true
              }
            }
            """;

        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)!;
        settings.SchemaVersion = SettingsSchemaV23.Version;

        Assert.True(settings.AppBehavior.KeepStatusVisibleDuringLocalBuildActivity);
        Assert.True(settings.AppBehavior.KeepStatusVisibleDuringAzureBuildActivity);
    }

    [Fact]
    public void Serialize_persists_status_visibility_settings()
    {
        var settings = new AppSettings
        {
            SchemaVersion = SettingsSchemaV23.Version,
            AppBehavior = new AppBehaviorSettings
            {
                KeepStatusVisibleDuringLocalBuildActivity = false,
                KeepStatusVisibleDuringAzureBuildActivity = true
            }
        };

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        Assert.Contains("\"keepStatusVisibleDuringLocalBuildActivity\": false", json, StringComparison.Ordinal);
        Assert.Contains("\"keepStatusVisibleDuringAzureBuildActivity\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\": 23", json, StringComparison.Ordinal);
    }
}
