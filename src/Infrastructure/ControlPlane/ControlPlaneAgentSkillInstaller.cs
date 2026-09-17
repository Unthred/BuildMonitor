using BuildMonitor.Core.Rules;

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
    string? RuleDestinationPath = null,
    string? BackupDirectory = null);

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
/// Copies the canonical verification-provider adapter into the current user's Cursor config.
/// Never writes into product repositories.
/// </summary>
public static class ControlPlaneAgentSkillInstaller
{
    public const string SkillFolderName = "buildmonitor-control-plane";
    public const string SkillFileName = "SKILL.md";
    public const string RuleFileName = "buildmonitor-control-plane.mdc";
    public const string BackupFolderName = "buildmonitor-adapter-backup";

    public static ControlPlaneAgentSkillInstallResult Install(
        string? projectRootFolder,
        string? explicitSkillSourcePath = null,
        string? explicitRuleSourcePath = null) =>
        InstallToUserCursor(
            ResolveUserProfileDirectory(),
            explicitSkillSourcePath,
            explicitRuleSourcePath);

    public static ControlPlaneAgentSkillInstallResult InstallToUserCursor(
        string userProfileDirectory,
        string? explicitSkillSourcePath = null,
        string? explicitRuleSourcePath = null,
        bool backupExisting = true)
    {
        if (string.IsNullOrWhiteSpace(userProfileDirectory))
        {
            return new ControlPlaneAgentSkillInstallResult(false, string.Empty, "User profile directory is empty.");
        }

        if (LooksLikeProductRepository(userProfileDirectory))
        {
            return new ControlPlaneAgentSkillInstallResult(
                false,
                string.Empty,
                "Refusing to install into a product repository. Use the user Cursor profile.");
        }

        if (!Directory.Exists(userProfileDirectory))
        {
            return new ControlPlaneAgentSkillInstallResult(
                false,
                string.Empty,
                $"User profile directory does not exist: {userProfileDirectory}");
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

        var skillDestPath = GetSkillPath(userProfileDirectory);
        var ruleDestPath = GetRulePath(userProfileDirectory);
        try
        {
            string? backupDirectory = null;
            if (backupExisting)
            {
                backupDirectory = BackupExisting(userProfileDirectory, skillDestPath, ruleDestPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(skillDestPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(ruleDestPath)!);
            File.Copy(skillSource, skillDestPath, overwrite: true);
            File.Copy(ruleSource, ruleDestPath, overwrite: true);
            return new ControlPlaneAgentSkillInstallResult(true, skillDestPath, null, ruleDestPath, backupDirectory);
        }
        catch (Exception ex)
        {
            return new ControlPlaneAgentSkillInstallResult(false, skillDestPath, ex.Message, ruleDestPath);
        }
    }

    public static ControlPlaneAgentIntegrationStatus Inspect(
        string? projectRootFolder,
        string? explicitSkillSourcePath = null,
        string? explicitRuleSourcePath = null) =>
        InspectUserCursor(ResolveUserProfileDirectory(), explicitSkillSourcePath, explicitRuleSourcePath);

    public static ControlPlaneAgentIntegrationStatus InspectUserCursor(
        string userProfileDirectory,
        string? explicitSkillSourcePath = null,
        string? explicitRuleSourcePath = null)
    {
        var skillPath = GetSkillPath(userProfileDirectory);
        var rulePath = GetRulePath(userProfileDirectory);
        if (string.IsNullOrWhiteSpace(userProfileDirectory) || !Directory.Exists(userProfileDirectory))
        {
            return new ControlPlaneAgentIntegrationStatus(
                ControlPlaneAgentIntegrationState.Missing,
                "Not installed",
                "User Cursor profile is missing.",
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
                "Install the user-level adapter so agents can claim exact worktrees. Product repos keep their own direct fallback.",
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
                $"Missing {missing}. Update the user-level adapter.",
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
                "User-level files do not match this BuildMonitor adapter version. Click Update.",
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
            $"User-level adapter {VerificationProviderAdapterMetadataParser.AdapterVersion} matches source {VerificationProviderAdapterMetadataParser.AdapterSourcePath}.",
            true,
            true,
            true,
            true,
            skillPath,
            rulePath,
            NeedsInstallOrUpdate: false);
    }

    public static string GetSkillPath(string userProfileDirectory) =>
        Path.Combine(userProfileDirectory ?? string.Empty, ".cursor", "skills", SkillFolderName, SkillFileName);

    public static string GetRulePath(string userProfileDirectory) =>
        Path.Combine(userProfileDirectory ?? string.Empty, ".cursor", "rules", RuleFileName);

    public static bool LooksLikeProductRepository(string directory) =>
        File.Exists(Path.Combine(directory, "WitherbyConnect.csproj"))
        || File.Exists(Path.Combine(directory, "WitherbyConnect.sln"))
        || File.Exists(Path.Combine(directory, "WitherbyConnect.slnx"));

    public static string ResolveUserProfileDirectory() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string? ResolveBundledSkillPath() =>
        ResolveBundledPath(SkillFolderName, SkillFileName);

    public static string? ResolveBundledRulePath() =>
        ResolveBundledPath(SkillFolderName, "RULE.mdc");

    private static string? BackupExisting(string userProfileDirectory, string skillDestPath, string ruleDestPath)
    {
        var skillExists = File.Exists(skillDestPath);
        var ruleExists = File.Exists(ruleDestPath);
        if (!skillExists && !ruleExists)
        {
            return null;
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var backupDir = Path.Combine(userProfileDirectory, ".cursor", BackupFolderName, stamp);
        Directory.CreateDirectory(backupDir);
        if (skillExists)
        {
            File.Copy(skillDestPath, Path.Combine(backupDir, SkillFileName), overwrite: true);
        }

        if (ruleExists)
        {
            File.Copy(ruleDestPath, Path.Combine(backupDir, RuleFileName), overwrite: true);
        }

        return backupDir;
    }

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
