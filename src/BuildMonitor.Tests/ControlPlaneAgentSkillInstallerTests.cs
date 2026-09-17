using BuildMonitor.Infrastructure.ControlPlane;

namespace BuildMonitor.Tests;

public sealed class ControlPlaneAgentSkillInstallerTests
{
    [Fact]
    public void InstallToUserCursor_copies_skill_and_rule_under_user_profile()
    {
        using var fx = new Fixture();
        var result = ControlPlaneAgentSkillInstaller.InstallToUserCursor(fx.UserProfile, fx.SkillSource, fx.RuleSource);
        Assert.True(result.Ok, result.Error);
        Assert.Equal(ControlPlaneAgentSkillInstaller.GetSkillPath(fx.UserProfile), result.DestinationPath);
        Assert.Equal(ControlPlaneAgentSkillInstaller.GetRulePath(fx.UserProfile), result.RuleDestinationPath);
        Assert.Contains("# test skill", File.ReadAllText(result.DestinationPath), StringComparison.Ordinal);
        Assert.Contains("# test rule", File.ReadAllText(result.RuleDestinationPath!), StringComparison.Ordinal);
        Assert.Null(result.BackupDirectory);
    }

    [Fact]
    public void InstallToUserCursor_refuses_product_repository()
    {
        using var fx = new Fixture();
        File.WriteAllText(Path.Combine(fx.UserProfile, "WitherbyConnect.csproj"), "<Project />");
        var result = ControlPlaneAgentSkillInstaller.InstallToUserCursor(fx.UserProfile, fx.SkillSource, fx.RuleSource);
        Assert.False(result.Ok);
        Assert.Contains("product repository", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(ControlPlaneAgentSkillInstaller.GetSkillPath(fx.UserProfile)));
    }

    [Fact]
    public void InstallToUserCursor_backs_up_existing_files()
    {
        using var fx = new Fixture();
        Assert.True(ControlPlaneAgentSkillInstaller.InstallToUserCursor(fx.UserProfile, fx.SkillSource, fx.RuleSource).Ok);
        File.WriteAllText(fx.SkillSource, "skill-v2");
        var updated = ControlPlaneAgentSkillInstaller.InstallToUserCursor(fx.UserProfile, fx.SkillSource, fx.RuleSource);
        Assert.True(updated.Ok, updated.Error);
        Assert.False(string.IsNullOrWhiteSpace(updated.BackupDirectory));
        Assert.True(File.Exists(Path.Combine(updated.BackupDirectory!, "SKILL.md")));
        Assert.Contains("# test skill", File.ReadAllText(Path.Combine(updated.BackupDirectory!, "SKILL.md")), StringComparison.Ordinal);
        Assert.Equal("skill-v2", File.ReadAllText(updated.DestinationPath).Trim());
    }

    [Fact]
    public void Inspect_reports_missing_when_nothing_installed()
    {
        using var fx = new Fixture();
        var status = ControlPlaneAgentSkillInstaller.InspectUserCursor(fx.UserProfile, fx.SkillSource, fx.RuleSource);
        Assert.Equal(ControlPlaneAgentIntegrationState.Missing, status.State);
        Assert.True(status.NeedsInstallOrUpdate);
        Assert.Equal("Not installed", status.Summary);
    }

    [Fact]
    public void Inspect_reports_partial_when_only_skill_present()
    {
        using var fx = new Fixture();
        Assert.True(ControlPlaneAgentSkillInstaller.InstallToUserCursor(fx.UserProfile, fx.SkillSource, fx.RuleSource).Ok);
        File.Delete(ControlPlaneAgentSkillInstaller.GetRulePath(fx.UserProfile));
        var status = ControlPlaneAgentSkillInstaller.InspectUserCursor(fx.UserProfile, fx.SkillSource, fx.RuleSource);
        Assert.Equal(ControlPlaneAgentIntegrationState.Partial, status.State);
        Assert.True(status.SkillPresent);
        Assert.False(status.RulePresent);
        Assert.True(status.NeedsInstallOrUpdate);
    }

    [Fact]
    public void Inspect_reports_outdated_then_current_after_update()
    {
        using var fx = new Fixture();
        Assert.True(ControlPlaneAgentSkillInstaller.InstallToUserCursor(fx.UserProfile, fx.SkillSource, fx.RuleSource).Ok);
        File.WriteAllText(fx.SkillSource, "skill-v2");
        File.WriteAllText(fx.RuleSource, "rule-v2");
        var outdated = ControlPlaneAgentSkillInstaller.InspectUserCursor(fx.UserProfile, fx.SkillSource, fx.RuleSource);
        Assert.Equal(ControlPlaneAgentIntegrationState.Outdated, outdated.State);
        Assert.True(outdated.NeedsInstallOrUpdate);

        Assert.True(ControlPlaneAgentSkillInstaller.InstallToUserCursor(fx.UserProfile, fx.SkillSource, fx.RuleSource).Ok);
        var current = ControlPlaneAgentSkillInstaller.InspectUserCursor(fx.UserProfile, fx.SkillSource, fx.RuleSource);
        Assert.Equal(ControlPlaneAgentIntegrationState.Current, current.State);
        Assert.False(current.NeedsInstallOrUpdate);
        Assert.Equal("Ready", current.Summary);
    }

    [Fact]
    public void Script_installs_to_temporary_destination_not_a_repo()
    {
        using var fx = new Fixture();
        var repoRoot = FindRepoRoot();
        var script = Path.Combine(repoRoot, "scripts", "Install-ControlPlaneAgentSkill.ps1");
        Assert.True(File.Exists(script), script);
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                script,
                "-DestinationRoot",
                fx.UserProfile
            }
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        Assert.True(proc.WaitForExit(30000));
        Assert.Equal(0, proc.ExitCode);
        Assert.True(File.Exists(ControlPlaneAgentSkillInstaller.GetSkillPath(fx.UserProfile)), stdout + stderr);
        Assert.True(File.Exists(ControlPlaneAgentSkillInstaller.GetRulePath(fx.UserProfile)), stdout + stderr);
        Assert.Contains("verificationProvider: true", File.ReadAllText(ControlPlaneAgentSkillInstaller.GetSkillPath(fx.UserProfile)), StringComparison.Ordinal);
    }

    [Fact]
    public void Script_refuses_product_repository_destination()
    {
        using var fx = new Fixture();
        File.WriteAllText(Path.Combine(fx.UserProfile, "WitherbyConnect.csproj"), "<Project />");
        var repoRoot = FindRepoRoot();
        var script = Path.Combine(repoRoot, "scripts", "Install-ControlPlaneAgentSkill.ps1");
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                script,
                "-DestinationRoot",
                fx.UserProfile
            }
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        Assert.True(proc.WaitForExit(30000));
        Assert.NotEqual(0, proc.ExitCode);
        Assert.Contains("product repository", stdout + stderr, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(ControlPlaneAgentSkillInstaller.GetSkillPath(fx.UserProfile)));
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BuildMonitor.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate BuildMonitor.slnx.");
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            UserProfile = Path.Combine(Path.GetTempPath(), "bm-user-" + Guid.NewGuid().ToString("N"));
            SourceDir = Path.Combine(Path.GetTempPath(), "bm-skill-src-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(UserProfile);
            Directory.CreateDirectory(SourceDir);
            SkillSource = Path.Combine(SourceDir, "SKILL.md");
            RuleSource = Path.Combine(SourceDir, "RULE.mdc");
            File.WriteAllText(SkillSource, "---\nname: buildmonitor-control-plane\n---\n# test skill\n");
            File.WriteAllText(RuleSource, "---\nalwaysApply: true\n---\n# test rule\n");
        }

        public string UserProfile { get; }
        public string SourceDir { get; }
        public string SkillSource { get; }
        public string RuleSource { get; }

        public void Dispose()
        {
            TryDelete(UserProfile);
            TryDelete(SourceDir);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // temp cleanup
            }
        }
    }
}
