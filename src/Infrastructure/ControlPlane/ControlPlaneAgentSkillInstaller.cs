namespace BuildMonitor.Infrastructure.ControlPlane;

public enum ControlPlaneAgentIntegrationState
{
    Missing = 0,
    Partial = 1,
    Outdated = 2,
    Current = 3
}

public sealed record ControlPlaneAgentSkillInstallResult(
    bool Ok,
    string DestinationPath,
    string? Error,
    string? RuleDestinationPath = null);

public sealed record ControlPlaneAgentIntegrationStatus(
    ControlPlaneAgentIntegrationState State,
    string Summary,
    string Detail,
    bool SkillPresent,
    bool SkillCurrent,
    bool RulePresent,
    bool RuleCurrent,
    string SkillPath,
    string RulePath,
    bool NeedsInstallOrUpdate);

/// <summary>
/// Copies the Cursor control-plane skill and always-on rule into a watched project's .cursor folder.
/// </summary>
public static class ControlPlaneAgentSkillInstaller
{
    public const string SkillFolderName = "buildmonitor-control-plane";
    public const string SkillFileName = "SKILL.md";
    public const string RuleFileName = "buildmonitor-control-plane.mdc";

    public static ControlPlaneAgentSkillInstallResult Install(
        string projectRootFolder,
        string? explicitSkillSourcePath = null,
        string? explicitRuleSourcePath = null)
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

        var skillSource = explicitSkillSourcePath ?? ResolveBundledSkillPath();
        if (skillSource is null || !File.Exists(skillSource))
        {
            return new ControlPlaneAgentSkillInstallResult(
                false,
                string.Empty,
                "Bundled skill file was not found next to BuildMonitor. Reinstall or rebuild the tray app.");
        }

        var ruleSource = explicitRuleSourcePath ?? ResolveBundledRulePath();
        if (ruleSource is null || !File.Exists(ruleSource))
        {
            return new ControlPlaneAgentSkillInstallResult(
                false,
                string.Empty,
                "Bundled always-on rule was not found next to BuildMonitor. Reinstall or rebuild the tray app.");
        }

        var skillDestDir = Path.Combine(projectRootFolder, ".cursor", "skills", SkillFolderName);
        var skillDestPath = Path.Combine(skillDestDir, SkillFileName);
        var ruleDestDir = Path.Combine(projectRootFolder, ".cursor", "rules");
        var ruleDestPath = Path.Combine(ruleDestDir, RuleFileName);
        try
        {
            Directory.CreateDirectory(skillDestDir);
            Directory.CreateDirectory(ruleDestDir);
            File.Copy(skillSource, skillDestPath, overwrite: true);
            File.Copy(ruleSource, ruleDestPath, overwrite: true);
            return new ControlPlaneAgentSkillInstallResult(true, skillDestPath, null, ruleDestPath);
        }
        catch (Exception ex)
        {
            return new ControlPlaneAgentSkillInstallResult(false, skillDestPath, ex.Message, ruleDestPath);
        }
    }

    public static ControlPlaneAgentIntegrationStatus Inspect(
        string projectRootFolder,
        string? explicitSkillSourcePath = null,
        string? explicitRuleSourcePath = null)
    {
        var skillPath = GetSkillPath(projectRootFolder);
        var rulePath = GetRulePath(projectRootFolder);
        if (string.IsNullOrWhiteSpace(projectRootFolder) || !Directory.Exists(projectRootFolder))
        {
            return new ControlPlaneAgentIntegrationStatus(
                ControlPlaneAgentIntegrationState.Missing,
                "Not installed",
                "Choose a valid project root folder first.",
                false,
                false,
                false,
                false,
                skillPath,
                rulePath,
                NeedsInstallOrUpdate: true);
        }

        var skillPresent = File.Exists(skillPath);
        var rulePresent = File.Exists(rulePath);
        var skillSource = explicitSkillSourcePath ?? ResolveBundledSkillPath();
        var ruleSource = explicitRuleSourcePath ?? ResolveBundledRulePath();
        var skillCurrent = skillPresent
            && skillSource is not null
            && FilesMatch(skillSource, skillPath);
        var ruleCurrent = rulePresent
            && ruleSource is not null
            && FilesMatch(ruleSource, rulePath);

        if (!skillPresent && !rulePresent)
        {
            return new ControlPlaneAgentIntegrationStatus(
                ControlPlaneAgentIntegrationState.Missing,
                "Not installed",
                "Agents in this repo will ask you to run raw dotnet build/test/watch. Click Install.",
                false,
                false,
                false,
                false,
                skillPath,
                rulePath,
                NeedsInstallOrUpdate: true);
        }

        if (!skillPresent || !rulePresent)
        {
            var missing = !skillPresent ? "skill" : "always-on rule";
            return new ControlPlaneAgentIntegrationStatus(
                ControlPlaneAgentIntegrationState.Partial,
                "Partially installed",
                $"Missing {missing}. Click Install / Update so agents handshake automatically.",
                skillPresent,
                skillCurrent,
                rulePresent,
                ruleCurrent,
                skillPath,
                rulePath,
                NeedsInstallOrUpdate: true);
        }

        if (!skillCurrent || !ruleCurrent)
        {
            return new ControlPlaneAgentIntegrationStatus(
                ControlPlaneAgentIntegrationState.Outdated,
                "Installed — update available",
                "Files are present but do not match this BuildMonitor version. Click Update.",
                true,
                skillCurrent,
                true,
                ruleCurrent,
                skillPath,
                rulePath,
                NeedsInstallOrUpdate: true);
        }

        return new ControlPlaneAgentIntegrationStatus(
            ControlPlaneAgentIntegrationState.Current,
            "Ready",
            "Skill + always-on rule are current. New agent chats in this folder use BuildMonitor without paste.",
            true,
            true,
            true,
            true,
            skillPath,
            rulePath,
            NeedsInstallOrUpdate: false);
    }

    public static string GetSkillPath(string projectRootFolder) =>
        Path.Combine(projectRootFolder ?? string.Empty, ".cursor", "skills", SkillFolderName, SkillFileName);

    public static string GetRulePath(string projectRootFolder) =>
        Path.Combine(projectRootFolder ?? string.Empty, ".cursor", "rules", RuleFileName);

    public static string? ResolveBundledSkillPath() =>
        ResolveBundledPath(SkillFolderName, SkillFileName);

    public static string? ResolveBundledRulePath() =>
        ResolveBundledPath(SkillFolderName, "RULE.mdc");

    private static string? ResolveBundledPath(string folderName, string fileName)
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "AgentSkills", folderName, fileName)
        };

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            candidates.Add(Path.Combine(
                dir.FullName,
                "docs",
                "ops",
                "agent-skills",
                folderName,
                fileName));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool FilesMatch(string leftPath, string rightPath)
    {
        try
        {
            var left = NormalizeText(File.ReadAllText(leftPath));
            var right = NormalizeText(File.ReadAllText(rightPath));
            return string.Equals(left, right, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeText(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
}
