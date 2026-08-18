using BuildMonitor.Infrastructure.ControlPlane;

namespace BuildMonitor.Tests;

public sealed class ControlPlaneAgentSkillInstallerTests
{
    [Fact]
    public void Install_copies_skill_and_rule_into_project_cursor_folders()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), "bm-skill-src-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(Path.GetTempPath(), "bm-skill-proj-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(sourceDir);
            var skillSource = Path.Combine(sourceDir, "SKILL.md");
            var ruleSource = Path.Combine(sourceDir, "RULE.mdc");
            File.WriteAllText(skillSource, "---\nname: buildmonitor-control-plane\n---\n# test skill\n");
            File.WriteAllText(ruleSource, "---\nalwaysApply: true\n---\n# test rule\n");
            Directory.CreateDirectory(projectDir);

            var result = ControlPlaneAgentSkillInstaller.Install(projectDir, skillSource, ruleSource);
            Assert.True(result.Ok, result.Error);
            Assert.True(File.Exists(result.DestinationPath));
            Assert.True(File.Exists(result.RuleDestinationPath));
            Assert.Contains("buildmonitor-control-plane", result.DestinationPath, StringComparison.Ordinal);
            Assert.Contains("buildmonitor-control-plane.mdc", result.RuleDestinationPath!, StringComparison.Ordinal);
            Assert.Contains("# test skill", File.ReadAllText(result.DestinationPath), StringComparison.Ordinal);
            Assert.Contains("# test rule", File.ReadAllText(result.RuleDestinationPath!), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDir(sourceDir);
            DeleteDir(projectDir);
        }
    }

    [Fact]
    public void Inspect_reports_missing_when_nothing_installed()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), "bm-skill-miss-" + Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(Path.GetTempPath(), "bm-skill-src-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(sourceDir);
            var skillSource = Path.Combine(sourceDir, "SKILL.md");
            var ruleSource = Path.Combine(sourceDir, "RULE.mdc");
            File.WriteAllText(skillSource, "skill");
            File.WriteAllText(ruleSource, "rule");

            var status = ControlPlaneAgentSkillInstaller.Inspect(projectDir, skillSource, ruleSource);
            Assert.Equal(ControlPlaneAgentIntegrationState.Missing, status.State);
            Assert.True(status.NeedsInstallOrUpdate);
            Assert.Equal("Not installed", status.Summary);
        }
        finally
        {
            DeleteDir(sourceDir);
            DeleteDir(projectDir);
        }
    }

    [Fact]
    public void Inspect_reports_partial_when_only_skill_present()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), "bm-skill-part-" + Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(Path.GetTempPath(), "bm-skill-src-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(sourceDir);
            var skillSource = Path.Combine(sourceDir, "SKILL.md");
            var ruleSource = Path.Combine(sourceDir, "RULE.mdc");
            File.WriteAllText(skillSource, "skill-v1");
            File.WriteAllText(ruleSource, "rule-v1");
            ControlPlaneAgentSkillInstaller.Install(projectDir, skillSource, ruleSource);
            File.Delete(ControlPlaneAgentSkillInstaller.GetRulePath(projectDir));

            var status = ControlPlaneAgentSkillInstaller.Inspect(projectDir, skillSource, ruleSource);
            Assert.Equal(ControlPlaneAgentIntegrationState.Partial, status.State);
            Assert.True(status.SkillPresent);
            Assert.False(status.RulePresent);
            Assert.True(status.NeedsInstallOrUpdate);
        }
        finally
        {
            DeleteDir(sourceDir);
            DeleteDir(projectDir);
        }
    }

    [Fact]
    public void Inspect_reports_outdated_then_current_after_update()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), "bm-skill-out-" + Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(Path.GetTempPath(), "bm-skill-src-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(sourceDir);
            var skillSource = Path.Combine(sourceDir, "SKILL.md");
            var ruleSource = Path.Combine(sourceDir, "RULE.mdc");
            File.WriteAllText(skillSource, "skill-v1");
            File.WriteAllText(ruleSource, "rule-v1");
            Assert.True(ControlPlaneAgentSkillInstaller.Install(projectDir, skillSource, ruleSource).Ok);

            File.WriteAllText(skillSource, "skill-v2");
            File.WriteAllText(ruleSource, "rule-v2");
            var outdated = ControlPlaneAgentSkillInstaller.Inspect(projectDir, skillSource, ruleSource);
            Assert.Equal(ControlPlaneAgentIntegrationState.Outdated, outdated.State);
            Assert.True(outdated.NeedsInstallOrUpdate);

            Assert.True(ControlPlaneAgentSkillInstaller.Install(projectDir, skillSource, ruleSource).Ok);
            var current = ControlPlaneAgentSkillInstaller.Inspect(projectDir, skillSource, ruleSource);
            Assert.Equal(ControlPlaneAgentIntegrationState.Current, current.State);
            Assert.False(current.NeedsInstallOrUpdate);
            Assert.Equal("Ready", current.Summary);
        }
        finally
        {
            DeleteDir(sourceDir);
            DeleteDir(projectDir);
        }
    }

    private static void DeleteDir(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
