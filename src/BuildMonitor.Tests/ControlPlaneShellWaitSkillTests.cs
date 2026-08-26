namespace BuildMonitor.Tests;

/// <summary>
/// Guards the authoritative control-plane skill wait contract (#81).
/// </summary>
public sealed class ControlPlaneShellWaitSkillTests
{
    [Fact]
    public void Canonical_skill_defines_short_poll_await_rules()
    {
        var path = Path.Combine(FindRepoRoot(), "docs", "ops", "agent-skills", "buildmonitor-control-plane", "SKILL.md");
        Assert.True(File.Exists(path), path);
        var text = File.ReadAllText(path);

        Assert.Contains("## Shell wait rules (authoritative)", text, StringComparison.Ordinal);
        Assert.Contains("maximum wait", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5–15 seconds", text, StringComparison.Ordinal);
        Assert.Contains(
            "Do **not** automatically background a BuildMonitor command and then call `AwaitShell` with a 5–10 minute timeout.",
            text,
            StringComparison.Ordinal);
        Assert.Contains("status=completed", text, StringComparison.Ordinal);
        Assert.Contains("Never wait for the remainder of a timeout budget", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_rule_points_to_skill_wait_section()
    {
        var path = Path.Combine(FindRepoRoot(), "docs", "ops", "agent-skills", "buildmonitor-control-plane", "RULE.mdc");
        Assert.True(File.Exists(path), path);
        var text = File.ReadAllText(path);
        Assert.Contains("multi-minute `AwaitShell`", text, StringComparison.Ordinal);
        Assert.Contains("Shell wait rules", text, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BuildMonitor.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Could not locate BuildMonitor.slnx from test BaseDirectory.");
    }
}
