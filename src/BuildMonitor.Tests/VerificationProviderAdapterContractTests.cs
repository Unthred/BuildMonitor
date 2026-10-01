using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class VerificationProviderAdapterContractTests
{
    [Fact]
    public void Canonical_skill_declares_provider_metadata()
    {
        var path = Path.Combine(FindRepoRoot(), "docs", "ops", "agent-skills", "buildmonitor-control-plane", "SKILL.md");
        var text = File.ReadAllText(path);
        var metadata = VerificationProviderAdapterMetadataParser.Parse(text);
        Assert.True(VerificationProviderAdapterMetadataParser.MatchesCanonical(metadata), text[..Math.Min(text.Length, 400)]);
        Assert.Contains("exact", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/run/ship-check", text, StringComparison.Ordinal);
        Assert.Contains("409", text, StringComparison.Ordinal);
        Assert.Contains("Verification: buildmonitor-control-plane", text, StringComparison.Ordinal);
        Assert.Contains("Do not also run direct `dotnet build`", text, StringComparison.Ordinal);
        Assert.DoesNotContain("parent/child OK", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("or a parent/child of it", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_rule_declines_unconfigured_worktrees()
    {
        var path = Path.Combine(FindRepoRoot(), "docs", "ops", "agent-skills", "buildmonitor-control-plane", "RULE.mdc");
        var text = File.ReadAllText(path);
        Assert.Contains("exact", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("direct-dotnet", text, StringComparison.Ordinal);
        Assert.Contains("Do not auto-configure", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("parent/child OK", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Feature_ship_treats_ship_as_not_merge_authorization()
    {
        var path = Path.Combine(FindRepoRoot(), ".cursor", "skills", "feature-ship", "SKILL.md");
        var text = File.ReadAllText(path);
        Assert.Contains("Publishing states", text, StringComparison.Ordinal);
        Assert.Contains("not merge authorization", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not human merge approval", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PR **merged** to `main` + issue closed (`Closes #N`) + project **Done**", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_rule_does_not_forbid_provider_and_dotnet_together()
    {
        var path = Path.Combine(FindRepoRoot(), ".cursor", "rules", "no-unapproved-runtime-execution.mdc");
        var text = File.ReadAllText(path);
        Assert.Contains("verification provider", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exclusively", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("**No exceptions**", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Claimed_parent_requires_registration_before_build()
    {
        Assert.Contains("registration is required before build", RuleText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("registration is required before build", SkillText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("POST /projects/register-worktree", RuleText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Successful_registration_makes_exact_path_claimable()
    {
        Assert.Contains("exact path becomes claimable", RuleText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact path becomes claimable", SkillText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("re-claim", SkillText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registration_failure_has_no_silent_direct_dotnet_fallback()
    {
        Assert.Contains("No silent direct-dotnet fallback", RuleText(), StringComparison.Ordinal);
        Assert.Contains("No silent direct-dotnet fallback", SkillText(), StringComparison.Ordinal);
        Assert.Contains("BuildMonitor: register failed", RuleText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Active_pull_request_does_not_unregister_or_remove_worktree()
    {
        Assert.Contains("active PR means no unregister", RuleText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("active PR means no unregister", SkillText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no worktree removal", RuleText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Merged_pull_request_unregisters_before_worktree_removal()
    {
        AssertUnregisterPrecedesWorktreeRemoval(RuleText());
        AssertUnregisterPrecedesWorktreeRemoval(SkillText());
    }

    [Fact]
    public void Manual_parent_is_never_automatically_unregistered()
    {
        Assert.Contains("must never be automatically unregistered", RuleText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must never be automatically unregistered", SkillText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("derivedFromProjectId", RuleText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_unregister_blocks_worktree_removal()
    {
        Assert.Contains("Failed unregister blocks worktree removal", RuleText(), StringComparison.Ordinal);
        Assert.Contains("Failed unregister blocks worktree removal", SkillText(), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMonitor_stays_optional_without_a_claimed_parent()
    {
        Assert.Contains("without a claimed parent", RuleText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("without a claimed parent", SkillText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not auto-configure", RuleText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("direct-dotnet", RuleText(), StringComparison.Ordinal);
        Assert.Contains("Do not auto-configure", SkillText(), StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertUnregisterPrecedesWorktreeRemoval(string text)
    {
        Assert.Contains("merged/completed", text, StringComparison.OrdinalIgnoreCase);
        var unregisterAt = text.IndexOf("/projects/unregister-worktree", StringComparison.Ordinal);
        var removeAt = text.IndexOf("git worktree remove", StringComparison.OrdinalIgnoreCase);
        Assert.True(unregisterAt >= 0 && removeAt > unregisterAt, text);
    }

    private static string RuleText() => Compact(File.ReadAllText(
        Path.Combine(FindRepoRoot(), "docs", "ops", "agent-skills", "buildmonitor-control-plane", "RULE.mdc")));

    private static string SkillText() => Compact(File.ReadAllText(
        Path.Combine(FindRepoRoot(), "docs", "ops", "agent-skills", "buildmonitor-control-plane", "SKILL.md")));

    private static string Compact(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
}
