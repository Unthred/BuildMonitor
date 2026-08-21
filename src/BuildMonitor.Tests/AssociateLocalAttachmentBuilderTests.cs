using BuildMonitor.Core.Settings;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class AssociateLocalAttachmentBuilderTests
{
    [Fact]
    public void Valid_folder_with_single_csproj_creates_complete_local_attachment()
    {
        var root = CreateTempRepo(withProject: true, projectName: "App.csproj");
        try
        {
            var project = AzureOnly("Azure project");
            var azure = project.Azure;
            var result = AssociateLocalAttachmentBuilder.TryBuild(project, root);
            Assert.Equal(AssociateLocalOutcome.Created, result.Outcome);
            Assert.NotNull(result.Local);
            Assert.False(string.IsNullOrWhiteSpace(result.Local!.ProjectFile));
            Assert.True(File.Exists(Path.Combine(result.Local.RootFolder, result.Local.ProjectFile)));

            Assert.True(AssociateLocalAttachmentBuilder.TryApply(project, result, out _));
            Assert.NotNull(project.Local);
            Assert.Same(azure, project.Azure);
            Assert.Equal("App", project.DisplayName);

            var errors = AppSettingsValidator.Validate(new AppSettings
            {
                Connections =
                [
                    new AzureDevOpsConnectionSettings
                    {
                        Id = azure!.ConnectionId,
                        DisplayName = "c",
                        OrganizationUrl = "https://dev.azure.com/org"
                    }
                ],
                Projects = [project]
            });
            Assert.Empty(errors);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void No_discoverable_project_leaves_local_null_and_preserves_azure()
    {
        var root = CreateTempRepo(withProject: false);
        try
        {
            var project = AzureOnly("Repo");
            var azure = project.Azure;
            var result = AssociateLocalAttachmentBuilder.TryBuild(project, root);
            Assert.Equal(AssociateLocalOutcome.NoCandidates, result.Outcome);
            Assert.Null(result.Local);
            Assert.Null(project.Local);
            Assert.Same(azure, project.Azure);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Cancel_when_multiple_candidates_leaves_local_null()
    {
        var root = CreateTempRepo(withProject: true, projectName: "A.csproj");
        File.WriteAllText(Path.Combine(root, "B.csproj"), "<Project />");
        try
        {
            var project = AzureOnly("Repo");
            var azure = project.Azure;
            var result = AssociateLocalAttachmentBuilder.TryBuild(
                project,
                root,
                pickWhenMultiple: _ => null);
            Assert.Equal(AssociateLocalOutcome.Cancelled, result.Outcome);
            Assert.Null(project.Local);
            Assert.Same(azure, project.Azure);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Multiple_candidates_with_selection_creates_valid_local()
    {
        var root = CreateTempRepo(withProject: true, projectName: "A.csproj");
        File.WriteAllText(Path.Combine(root, "B.csproj"), "<Project />");
        try
        {
            var project = AzureOnly("Repo");
            var result = AssociateLocalAttachmentBuilder.TryBuild(
                project,
                root,
                pickWhenMultiple: candidates => candidates.First(c => c.EndsWith("B.csproj", StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(AssociateLocalOutcome.Created, result.Outcome);
            Assert.EndsWith("B.csproj", result.Local!.ProjectFile, StringComparison.OrdinalIgnoreCase);
            Assert.True(AssociateLocalAttachmentBuilder.TryApply(project, result, out _));
            Assert.NotNull(project.Local);
            Assert.NotNull(project.Azure);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RootFolder_only_attachment_cannot_escape_builder()
    {
        var root = CreateTempRepo(withProject: false);
        try
        {
            var project = AzureOnly("Repo");
            var result = AssociateLocalAttachmentBuilder.TryBuild(project, root);
            Assert.NotEqual(AssociateLocalOutcome.Created, result.Outcome);
            Assert.Null(result.Local);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MonitoredProjectSettings AzureOnly(string repoName) =>
        new()
        {
            DisplayName = repoName,
            Local = null,
            Azure = new AzureDevOpsProjectAttachment
            {
                ConnectionId = "c1",
                AdoProjectId = "p1",
                AdoProjectName = "P",
                RepositoryId = "r1",
                RepositoryName = repoName
            }
        };

    private static string CreateTempRepo(bool withProject, string projectName = "App.csproj")
    {
        var root = Path.Combine(Path.GetTempPath(), "bm-assoc-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        File.WriteAllText(Path.Combine(root, "bin", "Ignore.csproj"), "<Project />");
        if (withProject)
        {
            File.WriteAllText(Path.Combine(root, projectName), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        }

        return root;
    }
}
