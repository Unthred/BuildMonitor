using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Tests;

public sealed class SettingsSchemaV26Tests
{
    [Fact]
    public void Apply_sets_StartOnLaunch_for_derived_projects_only()
    {
        var settings = new AppSettings
        {
            SchemaVersion = SettingsSchemaV25.Version,
            Projects =
            [
                new MonitoredProjectSettings
                {
                    Id = "main",
                    DisplayName = "Main",
                    Local = new LocalProjectAttachment
                    {
                        RootFolder = @"C:\src\Main",
                        ProjectFile = "App.csproj",
                        StartOnLaunch = false,
                        DerivedFromProjectId = null
                    }
                },
                new MonitoredProjectSettings
                {
                    Id = "derived",
                    DisplayName = "Derived",
                    Local = new LocalProjectAttachment
                    {
                        RootFolder = @"C:\src\Feature",
                        ProjectFile = "App.csproj",
                        StartOnLaunch = false,
                        DerivedFromProjectId = "main"
                    }
                }
            ]
        };

        SettingsSchemaV26.Apply(settings);

        Assert.False(settings.Projects[0].Local!.StartOnLaunch);
        Assert.True(settings.Projects[1].Local!.StartOnLaunch);
    }
}
