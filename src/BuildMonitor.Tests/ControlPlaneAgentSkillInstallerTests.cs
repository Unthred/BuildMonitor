using BuildMonitor.Infrastructure.ControlPlane;

namespace BuildMonitor.Tests;

public sealed class ControlPlaneAgentSkillInstallerTests
{
    [Fact]
    public void Install_copies_skill_into_project_cursor_skills()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), "bm-skill-src-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(Path.GetTempPath(), "bm-skill-proj-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(sourceDir);
            var source = Path.Combine(sourceDir, "SKILL.md");
            File.WriteAllText(source, "---\nname: buildmonitor-control-plane\n---\n# test\n");
            Directory.CreateDirectory(projectDir);

            var result = ControlPlaneAgentSkillInstaller.Install(projectDir, source);
            Assert.True(result.Ok, result.Error);
            Assert.True(File.Exists(result.DestinationPath));
            Assert.Contains("buildmonitor-control-plane", result.DestinationPath, StringComparison.Ordinal);
            Assert.Contains("# test", File.ReadAllText(result.DestinationPath), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, true);
            }

            if (Directory.Exists(projectDir))
            {
                Directory.Delete(projectDir, true);
            }
        }
    }
}
