using System.Text.Json;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Tests;

public sealed class SettingsSchemaV21Tests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [Fact]
    public void FromLegacyV20_preserves_all_local_settings_and_emits_nested_shape()
    {
        var legacy = new LegacyAppSettingsV20
        {
            SchemaVersion = 20,
            Projects =
            [
                new LegacyFlatProjectSettings
                {
                    Id = "abc123",
                    DisplayName = "Vessel",
                    IsActiveInSession = true,
                    RootFolder = @"C:\src\Vessel",
                    ProjectFile = "Vessel.csproj",
                    LaunchProfile = "https",
                    ExtraDotNetArgs = "--no-restore",
                    TestProjectFile = "Vessel.Tests.csproj",
                    StartOnLaunch = false,
                    BuildControlMode = ProjectBuildControlMode.AiControlled,
                    PreferredSiteUrlScheme = PreferredSiteUrlScheme.Https,
                    RunOptions = new ProjectRunOptions
                    {
                        RunMode = ProjectRunMode.Run,
                        RestartOnCrash = false,
                        MaxRestartRetries = 2,
                        AutoOpenLog = AutoOpenLogMode.Errors,
                        ShowStatusPanelWhileBuilding = false,
                        ForceCompleteWarningCounts = false
                    }
                }
            ]
        };

        var settings = SettingsSchemaV21.FromLegacyV20(legacy);

        Assert.Equal(21, settings.SchemaVersion);
        Assert.Empty(settings.Connections);
        var project = Assert.Single(settings.Projects);
        Assert.Equal("abc123", project.Id);
        Assert.Equal("Vessel", project.DisplayName);
        Assert.True(project.IsActiveInSession);
        Assert.Null(project.Azure);
        Assert.NotNull(project.Local);
        Assert.Equal(@"C:\src\Vessel", project.Local.RootFolder);
        Assert.Equal("Vessel.csproj", project.Local.ProjectFile);
        Assert.Equal("https", project.Local.LaunchProfile);
        Assert.Equal("--no-restore", project.Local.ExtraDotNetArgs);
        Assert.Equal("Vessel.Tests.csproj", project.Local.TestProjectFile);
        Assert.False(project.Local.StartOnLaunch);
        Assert.Equal(ProjectBuildControlMode.AiControlled, project.Local.BuildControlMode);
        Assert.Equal(PreferredSiteUrlScheme.Https, project.Local.PreferredSiteUrlScheme);
        Assert.Equal(ProjectRunMode.Run, project.Local.RunOptions.RunMode);
        Assert.False(project.Local.RunOptions.RestartOnCrash);
        Assert.Equal(2, project.Local.RunOptions.MaxRestartRetries);
        Assert.Equal(AutoOpenLogMode.Errors, project.Local.RunOptions.AutoOpenLog);
        Assert.False(project.Local.RunOptions.ShowStatusPanelWhileBuilding);
        Assert.False(project.Local.RunOptions.ForceCompleteWarningCounts);

        var saved = JsonSerializer.Serialize(settings, JsonOptions);
        Assert.Contains("\"schemaVersion\": 21", saved, StringComparison.Ordinal);
        Assert.Contains("\"local\"", saved, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"pat\"", saved, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", saved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Legacy_json_deserializes_into_flat_dto_then_migrates()
    {
        var json = """
            {
              "schemaVersion": 20,
              "projects": [
                {
                  "id": "p1",
                  "displayName": "Demo",
                  "rootFolder": "C:\\demo",
                  "projectFile": "Demo.csproj",
                  "isActiveInSession": true,
                  "runOptions": { "runMode": 2 }
                }
              ],
              "monitor": {},
              "appBehavior": {}
            }
            """;

        var legacy = JsonSerializer.Deserialize<LegacyAppSettingsV20>(json, JsonOptions);
        Assert.NotNull(legacy);
        var settings = SettingsSchemaV21.FromLegacyV20(legacy);
        var project = Assert.Single(settings.Projects);
        Assert.Equal("p1", project.Id);
        Assert.NotNull(project.Local);
        Assert.Equal(ProjectRunMode.Watch, project.Local.RunOptions.RunMode);
    }
}

public sealed class AppSettingsValidatorAttachmentTests
{
    [Fact]
    public void Local_only_valid_when_paths_exist()
    {
        var root = CreateTempProjectTree(out var projectFile);
        try
        {
            var settings = new AppSettings
            {
                Projects =
                [
                    new MonitoredProjectSettings
                    {
                        DisplayName = "Local",
                        Local = new LocalProjectAttachment
                        {
                            RootFolder = root,
                            ProjectFile = projectFile
                        }
                    }
                ]
            };

            Assert.Empty(AppSettingsValidator.Validate(settings));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Azure_only_valid_with_connection_and_zero_pipelines()
    {
        var connectionId = Guid.NewGuid().ToString("N");
        var settings = new AppSettings
        {
            Connections =
            [
                new AzureDevOpsConnectionSettings
                {
                    Id = connectionId,
                    DisplayName = "Org",
                    OrganizationUrl = "https://dev.azure.com/contoso"
                }
            ],
            Projects =
            [
                new MonitoredProjectSettings
                {
                    DisplayName = "Cloud",
                    Azure = new AzureDevOpsProjectAttachment
                    {
                        ConnectionId = connectionId,
                        AdoProjectName = "BuildMonitor",
                        RepositoryId = "repo-1",
                        RepositoryName = "BuildMonitor",
                        Pipelines = []
                    }
                }
            ]
        };

        Assert.Empty(AppSettingsValidator.Validate(settings));
    }

    [Fact]
    public void Local_plus_azure_valid()
    {
        var root = CreateTempProjectTree(out var projectFile);
        var connectionId = Guid.NewGuid().ToString("N");
        try
        {
            var settings = new AppSettings
            {
                Connections =
                [
                    new AzureDevOpsConnectionSettings
                    {
                        Id = connectionId,
                        DisplayName = "Org",
                        OrganizationUrl = "https://dev.azure.com/contoso"
                    }
                ],
                Projects =
                [
                    new MonitoredProjectSettings
                    {
                        DisplayName = "Both",
                        Local = new LocalProjectAttachment
                        {
                            RootFolder = root,
                            ProjectFile = projectFile
                        },
                        Azure = new AzureDevOpsProjectAttachment
                        {
                            ConnectionId = connectionId,
                            AdoProjectId = "guid",
                            RepositoryName = "repo",
                            Pipelines =
                            [
                                new AzurePipelineSelection { DefinitionId = 42, DisplayName = "CI" }
                            ]
                        }
                    }
                ]
            };

            Assert.Empty(AppSettingsValidator.Validate(settings));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Neither_attachment_is_invalid()
    {
        var settings = new AppSettings
        {
            Projects = [new MonitoredProjectSettings { DisplayName = "Empty" }]
        };

        var errors = AppSettingsValidator.Validate(settings);
        Assert.Contains(errors, e => e.Contains("at least one", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Invalid_connection_id_is_invalid()
    {
        var settings = new AppSettings
        {
            Connections =
            [
                new AzureDevOpsConnectionSettings
                {
                    Id = "conn-1",
                    OrganizationUrl = "https://dev.azure.com/contoso"
                }
            ],
            Projects =
            [
                new MonitoredProjectSettings
                {
                    DisplayName = "Bad",
                    Azure = new AzureDevOpsProjectAttachment
                    {
                        ConnectionId = "missing",
                        AdoProjectName = "P",
                        RepositoryId = "R"
                    }
                }
            ]
        };

        var errors = AppSettingsValidator.Validate(settings);
        Assert.Contains(errors, e => e.Contains("ConnectionId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Serialized_settings_do_not_contain_credential_fields()
    {
        var settings = new AppSettings
        {
            Connections =
            [
                new AzureDevOpsConnectionSettings
                {
                    Id = "c1",
                    DisplayName = "Org",
                    OrganizationUrl = "https://dev.azure.com/contoso"
                }
            ]
        };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.DoesNotContain("pat", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(AzureCiMonitoringState.NotMonitored, AzureMonitoringAvailability.Available, null)]
    [InlineData(AzureCiMonitoringState.Healthy, AzureMonitoringAvailability.Available, MonitorHealth.Green)]
    [InlineData(AzureCiMonitoringState.Failed, AzureMonitoringAvailability.Available, MonitorHealth.Red)]
    [InlineData(AzureCiMonitoringState.Healthy, AzureMonitoringAvailability.AuthRequired, MonitorHealth.Amber)]
    [InlineData(AzureCiMonitoringState.NotMonitored, AzureMonitoringAvailability.AuthRequired, MonitorHealth.Amber)]
    [InlineData(AzureCiMonitoringState.Activity, AzureMonitoringAvailability.Unavailable, MonitorHealth.Amber)]
    public void Azure_health_contribution_contract(
        AzureCiMonitoringState ci,
        AzureMonitoringAvailability availability,
        MonitorHealth? expected) =>
        Assert.Equal(expected, AzureHealthContribution.ToTrayContribution(ci, availability));

    private static string CreateTempProjectTree(out string relativeProjectFile)
    {
        var root = Path.Combine(Path.GetTempPath(), $"bm-proj-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        relativeProjectFile = "App.csproj";
        File.WriteAllText(Path.Combine(root, relativeProjectFile), "<Project />");
        return root;
    }
}
