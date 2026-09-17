using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class ProjectRunContextFactoryTests : IDisposable
{
    private readonly string firstRoot;
    private readonly string secondRoot;

    public ProjectRunContextFactoryTests()
    {
        firstRoot = CreateTree("first", "https://localhost:44333;http://localhost:5154");
        secondRoot = CreateTree("second", "https://localhost:44349;http://localhost:5170");
    }

    [Fact]
    public void Capture_uses_the_initiating_project_paths_not_current_directory_or_first_tree()
    {
        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(firstRoot);

        try
        {
            var project = new MonitoredProjectSettings
            {
                Id = "worktree",
                DisplayName = "2ndWitherbyConnect (main)",
                Local = new LocalProjectAttachment
                {
                    RootFolder = secondRoot,
                    ProjectFile = "WitherbyConnect.csproj",
                    LaunchProfile = "https",
                    ApplicationUrl = "https://localhost:44349",
                    ExtraDotNetArgs = "--no-restore"
                }
            };

            var context = ProjectRunContextFactory.Capture(project);

            Assert.Equal("worktree", context.ProjectId);
            Assert.Equal(Path.GetFullPath(secondRoot), context.RootFolder);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(secondRoot, "WitherbyConnect.csproj")),
                context.StartupProjectPath);
            Assert.Equal(Path.GetFullPath(secondRoot), context.WorkingDirectory);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(secondRoot, "Properties", "launchSettings.json")),
                context.LaunchSettingsPath);
            Assert.DoesNotContain("first", context.LaunchSettingsPath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("https://localhost:44349", context.EffectiveApplicationUrl);
            Assert.Equal("https", context.LaunchProfile);
            Assert.Equal("--no-restore", context.ExtraDotNetArgs);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    [Fact]
    public void Capture_is_immutable_after_later_settings_or_selection_changes()
    {
        var project = new MonitoredProjectSettings
        {
            Id = "worktree",
            DisplayName = "Worktree",
            Local = new LocalProjectAttachment
            {
                RootFolder = secondRoot,
                ProjectFile = "WitherbyConnect.csproj",
                LaunchProfile = "https",
                ApplicationUrl = "https://localhost:44349",
                ExtraDotNetArgs = "--no-restore"
            }
        };

        var context = ProjectRunContextFactory.Capture(project);
        project.DisplayName = "Selected first project";
        project.Local.RootFolder = firstRoot;
        project.Local.ProjectFile = "Other.csproj";
        project.Local.LaunchProfile = "http";
        project.Local.ApplicationUrl = "https://localhost:44333";
        project.Local.ExtraDotNetArgs = "--stolen";

        Assert.Equal("worktree", context.ProjectId);
        Assert.Equal("Worktree", context.DisplayName);
        Assert.Equal(Path.GetFullPath(secondRoot), context.RootFolder);
        Assert.Equal("https", context.LaunchProfile);
        Assert.Equal("https://localhost:44349", context.ApplicationUrlOverride);
        Assert.Equal("--no-restore", context.ExtraDotNetArgs);
        Assert.DoesNotContain("first", context.LaunchSettingsPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capture_resolves_launchSettings_next_to_a_nested_startup_csproj()
    {
        var nested = Path.Combine(secondRoot, "src", "Web");
        Directory.CreateDirectory(Path.Combine(nested, "Properties"));
        File.WriteAllText(Path.Combine(nested, "Web.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>");
        File.WriteAllText(
            Path.Combine(nested, "Properties", "launchSettings.json"),
            """
            {
              "profiles": {
                "https": {
                  "commandName": "Project",
                  "applicationUrl": "https://localhost:7001"
                }
              }
            }
            """);

        var project = new MonitoredProjectSettings
        {
            Id = "nested",
            DisplayName = "Nested",
            Local = new LocalProjectAttachment
            {
                RootFolder = secondRoot,
                ProjectFile = Path.Combine("src", "Web", "Web.csproj"),
                LaunchProfile = "https"
            }
        };

        var context = ProjectRunContextFactory.Capture(project);

        Assert.Equal(Path.GetFullPath(nested), context.WorkingDirectory);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(nested, "Properties", "launchSettings.json")),
            context.LaunchSettingsPath);
        Assert.Equal("https://localhost:7001", context.EffectiveApplicationUrl);
    }

    public void Dispose()
    {
        TryDelete(firstRoot);
        TryDelete(secondRoot);
    }

    private static string CreateTree(string name, string applicationUrl)
    {
        var root = Path.Combine(Path.GetTempPath(), "bm-ctx-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Properties"));
        File.WriteAllText(Path.Combine(root, "WitherbyConnect.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>");
        File.WriteAllText(
            Path.Combine(root, "Properties", "launchSettings.json"),
            $$"""
            {
              "profiles": {
                "https": {
                  "commandName": "Project",
                  "applicationUrl": "{{applicationUrl}}"
                }
              }
            }
            """);
        return root;
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
