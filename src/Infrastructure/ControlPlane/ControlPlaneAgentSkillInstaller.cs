namespace BuildMonitor.Infrastructure.ControlPlane;

public sealed record ControlPlaneAgentSkillInstallResult(
    bool Ok,
    string DestinationPath,
    string? Error);

/// <summary>Copies the Cursor control-plane skill into a watched project's .cursor/skills folder.</summary>
public static class ControlPlaneAgentSkillInstaller
{
    public const string SkillFolderName = "buildmonitor-control-plane";
    public const string SkillFileName = "SKILL.md";

    public static ControlPlaneAgentSkillInstallResult Install(
        string projectRootFolder,
        string? explicitSkillSourcePath = null)
    {
        if (string.IsNullOrWhiteSpace(projectRootFolder))
        {
            return new ControlPlaneAgentSkillInstallResult(false, string.Empty, "Project root folder is empty.");
        }

        if (!Directory.Exists(projectRootFolder))
        {
            return new ControlPlaneAgentSkillInstallResult(
                false,
                string.Empty,
                $"Root folder does not exist: {projectRootFolder}");
        }

        var source = explicitSkillSourcePath ?? ResolveBundledSkillPath();
        if (source is null || !File.Exists(source))
        {
            return new ControlPlaneAgentSkillInstallResult(
                false,
                string.Empty,
                "Bundled skill file was not found next to BuildMonitor. Reinstall or rebuild the tray app.");
        }

        var destDir = Path.Combine(projectRootFolder, ".cursor", "skills", SkillFolderName);
        var destPath = Path.Combine(destDir, SkillFileName);
        try
        {
            Directory.CreateDirectory(destDir);
            File.Copy(source, destPath, overwrite: true);
            return new ControlPlaneAgentSkillInstallResult(true, destPath, null);
        }
        catch (Exception ex)
        {
            return new ControlPlaneAgentSkillInstallResult(false, destPath, ex.Message);
        }
    }

    public static string? ResolveBundledSkillPath()
    {
        var candidates = new List<string>
        {
            Path.Combine(
                AppContext.BaseDirectory,
                "AgentSkills",
                SkillFolderName,
                SkillFileName)
        };

        // Dev: running from bin/... — walk up toward repo docs/ops/agent-skills.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            candidates.Add(Path.Combine(
                dir.FullName,
                "docs",
                "ops",
                "agent-skills",
                SkillFolderName,
                SkillFileName));
        }

        return candidates.FirstOrDefault(File.Exists);
    }
}
