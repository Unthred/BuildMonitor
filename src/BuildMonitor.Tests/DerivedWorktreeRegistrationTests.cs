using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.Git;
using BuildMonitor.Infrastructure.Services;

namespace BuildMonitor.Tests;

public sealed class DerivedWorktreeRegistrationTests
{
    [Fact]
    public void Factory_inherits_local_settings_and_marks_derived()
    {
        var parent = CreateParent(@"C:\src\App");
        var derived = DerivedWorktreeProjectFactory.Create(
            parent,
            @"C:\src\App-feature",
            "derived1",
            "https://localhost:44349");

        Assert.Equal("derived1", derived.Id);
        Assert.Equal("App (App-feature)", derived.DisplayName);
        Assert.True(derived.IsActiveInSession);
        Assert.NotNull(derived.Local);
        Assert.Equal(@"C:\src\App-feature", derived.Local.RootFolder);
        Assert.Equal("App.csproj", derived.Local.ProjectFile);
        Assert.Equal("https", derived.Local.LaunchProfile);
        Assert.Equal("https://localhost:44349", derived.Local.ApplicationUrl);
        Assert.False(derived.Local.StartOnLaunch);
        Assert.Equal(parent.Id, derived.Local.DerivedFromProjectId);
        Assert.Equal(ProjectBuildControlMode.AiControlled, derived.Local.BuildControlMode);
        Assert.Equal(ProjectRunMode.Watch, derived.Local.RunOptions.RunMode);
        Assert.NotNull(derived.Azure);
        Assert.Equal("repo-1", derived.Azure!.RepositoryId);
    }

    [Fact]
    public void Factory_remaps_absolute_paths_under_parent_root()
    {
        var remapped = DerivedWorktreeProjectFactory.RemapPathUnderRoot(
            @"C:\src\App\tests\App.Tests.csproj",
            @"C:\src\App",
            @"C:\src\App-wt");

        Assert.Equal(
            Path.GetFullPath(@"C:\src\App-wt\tests\App.Tests.csproj"),
            Path.GetFullPath(remapped!));
    }

    [Fact]
    public void Git_identity_compares_common_dir_not_folder_name()
    {
        Assert.True(GitWorktreeIdentity.SameRepository(
            @"C:\src\App\.git",
            @"C:\src\App\.git\"));
        Assert.False(GitWorktreeIdentity.SameRepository(
            @"C:\src\App\.git",
            @"C:\src\Other\.git"));
        Assert.False(GitWorktreeIdentity.SameRepository(null, @"C:\src\App\.git"));
    }

    [Fact]
    public async Task Register_from_claimed_parent_allocates_unique_ports()
    {
        using var scope = new OrchestratorScope();
        var parentRoot = scope.CreateGitishFolder("parent");
        var worktreeRoot = scope.CreateGitishFolder("worktree");
        WriteLaunchSettings(worktreeRoot, "https://localhost:44333;http://localhost:5154");
        WriteLaunchSettings(parentRoot, "https://localhost:44333;http://localhost:5154");

        var parent = CreateParent(parentRoot, applicationUrl: "https://localhost:44333;http://localhost:5154");
        scope.Orchestrator.ApplySettings(new AppSettings
        {
            SchemaVersion = SettingsSchemaV25.Version,
            Projects = [parent]
        });
        scope.Orchestrator.SetGitWorktreeIdentityReader(new FakeGitIdentity(parentRoot, worktreeRoot));

        var result = await scope.Orchestrator.RegisterDerivedWorktreeAsync(
            new ControlPlaneRegisterWorktreeRequest(parent.Id, worktreeRoot),
            CancellationToken.None);

        Assert.True(result.Ok, result.Error);
        Assert.True(result.Created);
        Assert.Equal("https://localhost:44349;http://localhost:5170", result.ApplicationUrl);
        Assert.Equal(parent.Id, result.ParentProjectId);

        var settings = scope.LastPersisted!;
        var derived = settings.Projects.Single(p => p.Id == result.ProjectId);
        Assert.Equal(parent.Id, derived.Local!.DerivedFromProjectId);
        Assert.False(derived.Local.StartOnLaunch);
        Assert.Equal(result.ApplicationUrl, derived.Local.ApplicationUrl);
        Assert.DoesNotContain(
            Path.Combine(worktreeRoot, "BuildMonitor"),
            Directory.GetFileSystemEntries(worktreeRoot),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_rejects_unrelated_repository_keeps_settings()
    {
        using var scope = new OrchestratorScope();
        var parentRoot = scope.CreateGitishFolder("parent");
        var otherRoot = scope.CreateGitishFolder("other");
        var parent = CreateParent(parentRoot);
        var settings = new AppSettings
        {
            SchemaVersion = SettingsSchemaV25.Version,
            Projects = [parent]
        };
        scope.Orchestrator.ApplySettings(settings);
        scope.Orchestrator.SetGitWorktreeIdentityReader(
            new FakeGitIdentity(
                (parentRoot, @"C:\git\App\.git"),
                (otherRoot, @"C:\git\Other\.git")));

        var result = await scope.Orchestrator.RegisterDerivedWorktreeAsync(
            new ControlPlaneRegisterWorktreeRequest(parent.Id, otherRoot),
            CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("different Git repository", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(scope.LastPersisted);
        Assert.Single(settings.Projects);
    }

    [Fact]
    public async Task Register_same_path_is_idempotent()
    {
        using var scope = new OrchestratorScope();
        var parentRoot = scope.CreateGitishFolder("parent");
        var worktreeRoot = scope.CreateGitishFolder("worktree");
        WriteLaunchSettings(worktreeRoot, "https://localhost:44333");
        WriteLaunchSettings(parentRoot, "https://localhost:44333");

        var parent = CreateParent(parentRoot, applicationUrl: "https://localhost:44333");
        scope.Orchestrator.ApplySettings(new AppSettings
        {
            SchemaVersion = SettingsSchemaV25.Version,
            Projects = [parent]
        });
        scope.Orchestrator.SetGitWorktreeIdentityReader(new FakeGitIdentity(parentRoot, worktreeRoot));

        var first = await scope.Orchestrator.RegisterDerivedWorktreeAsync(
            new ControlPlaneRegisterWorktreeRequest(parent.Id, worktreeRoot),
            CancellationToken.None);
        Assert.True(first.Created);

        var second = await scope.Orchestrator.RegisterDerivedWorktreeAsync(
            new ControlPlaneRegisterWorktreeRequest(parent.Id, worktreeRoot),
            CancellationToken.None);

        Assert.True(second.Ok);
        Assert.True(second.AlreadyRegistered);
        Assert.Equal(first.ProjectId, second.ProjectId);
        Assert.Equal(2, scope.LastPersisted!.Projects.Count);
    }

    [Fact]
    public async Task Unregister_removes_derived_only_and_is_idempotent()
    {
        using var scope = new OrchestratorScope();
        var parentRoot = scope.CreateGitishFolder("parent");
        var worktreeRoot = scope.CreateGitishFolder("worktree");
        WriteLaunchSettings(worktreeRoot, "https://localhost:44333");
        WriteLaunchSettings(parentRoot, "https://localhost:44333");
        var parent = CreateParent(parentRoot, applicationUrl: "https://localhost:44333");
        scope.Orchestrator.ApplySettings(new AppSettings
        {
            SchemaVersion = SettingsSchemaV25.Version,
            Projects = [parent]
        });
        scope.Orchestrator.SetGitWorktreeIdentityReader(new FakeGitIdentity(parentRoot, worktreeRoot));

        var registered = await scope.Orchestrator.RegisterDerivedWorktreeAsync(
            new ControlPlaneRegisterWorktreeRequest(parent.Id, worktreeRoot),
            CancellationToken.None);
        Assert.True(registered.Created);

        var removed = await scope.Orchestrator.UnregisterDerivedWorktreeAsync(
            new ControlPlaneUnregisterWorktreeRequest(registered.ProjectId!),
            CancellationToken.None);
        Assert.True(removed.Ok);
        Assert.True(removed.Removed);
        Assert.Single(scope.LastPersisted!.Projects);
        Assert.Equal(parent.Id, scope.LastPersisted.Projects[0].Id);
        Assert.True(Directory.Exists(worktreeRoot));

        var again = await scope.Orchestrator.UnregisterDerivedWorktreeAsync(
            new ControlPlaneUnregisterWorktreeRequest(registered.ProjectId!),
            CancellationToken.None);
        Assert.True(again.Ok);
        Assert.True(again.AlreadyRemoved);
    }

    [Fact]
    public async Task Unregister_refuses_non_derived_project()
    {
        using var scope = new OrchestratorScope();
        var parentRoot = scope.CreateGitishFolder("parent");
        var parent = CreateParent(parentRoot);
        scope.Orchestrator.ApplySettings(new AppSettings
        {
            SchemaVersion = SettingsSchemaV25.Version,
            Projects = [parent]
        });

        var result = await scope.Orchestrator.UnregisterDerivedWorktreeAsync(
            new ControlPlaneUnregisterWorktreeRequest(parent.Id),
            CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("not a derived worktree", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(scope.LastPersisted);
    }

    private static MonitoredProjectSettings CreateParent(
        string rootFolder,
        string? applicationUrl = null)
    {
        return new MonitoredProjectSettings
        {
            Id = "parent1",
            DisplayName = "App",
            IsActiveInSession = true,
            Local = new LocalProjectAttachment
            {
                RootFolder = rootFolder,
                ProjectFile = "App.csproj",
                LaunchProfile = "https",
                ApplicationUrl = applicationUrl ?? string.Empty,
                StartOnLaunch = true,
                BuildControlMode = ProjectBuildControlMode.AiControlled,
                RunOptions = new ProjectRunOptions { RunMode = ProjectRunMode.Watch }
            },
            Azure = new AzureDevOpsProjectAttachment
            {
                ConnectionId = "conn",
                RepositoryId = "repo-1",
                RepositoryName = "App"
            }
        };
    }

    private static void WriteLaunchSettings(string root, string applicationUrl)
    {
        var props = Path.Combine(root, "Properties");
        Directory.CreateDirectory(props);
        File.WriteAllText(
            Path.Combine(props, "launchSettings.json"),
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
        File.WriteAllText(Path.Combine(root, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
    }

    private sealed class OrchestratorScope : IDisposable
    {
        private readonly string root;

        public OrchestratorScope()
        {
            root = Path.Combine(Path.GetTempPath(), "bm-wt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Orchestrator = new ProjectOrchestrator(Path.Combine(root, "logs"), root);
            Orchestrator.SetSettingsPersistHandler(s => LastPersisted = CloneSettings(s));
        }

        public ProjectOrchestrator Orchestrator { get; }
        public AppSettings? LastPersisted { get; private set; }

        public string CreateGitishFolder(string name)
        {
            var path = Path.Combine(root, name);
            Directory.CreateDirectory(path);
            Directory.CreateDirectory(Path.Combine(path, ".git"));
            return path;
        }

        public void Dispose()
        {
            Orchestrator.Dispose();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best-effort temp cleanup
            }
        }

        private static AppSettings CloneSettings(AppSettings settings)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(settings);
            return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        }
    }

    private sealed class FakeGitIdentity : IGitWorktreeIdentityReader
    {
        private readonly Dictionary<string, string> commonDirs;

        public FakeGitIdentity(params string[] sameRepoPaths)
        {
            commonDirs = sameRepoPaths.ToDictionary(
                VerificationProviderPath.Normalize,
                _ => @"C:\git\shared\.git",
                StringComparer.OrdinalIgnoreCase);
        }

        public FakeGitIdentity(params (string Path, string CommonDir)[] entries)
        {
            commonDirs = entries.ToDictionary(
                e => VerificationProviderPath.Normalize(e.Path),
                e => GitWorktreeIdentity.NormalizeCommonDir(e.CommonDir),
                StringComparer.OrdinalIgnoreCase);
        }

        public Task<GitWorktreeIdentityInfo?> TryReadAsync(string path, CancellationToken cancellationToken)
        {
            var key = VerificationProviderPath.Normalize(path);
            if (!commonDirs.TryGetValue(key, out var common))
            {
                return Task.FromResult<GitWorktreeIdentityInfo?>(null);
            }

            return Task.FromResult<GitWorktreeIdentityInfo?>(new GitWorktreeIdentityInfo(true, common));
        }
    }
}
