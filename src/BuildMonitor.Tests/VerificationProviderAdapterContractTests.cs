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
