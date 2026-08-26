namespace BuildMonitor.Tests;

/// <summary>
/// Guards against reintroducing Settings copy that claims Azure monitoring is unavailable
/// after continuous polling shipped (#30 / #77).
/// </summary>
public sealed class AzureMonitoringCopyRegressionTests
{
    [Fact]
    public void Settings_sources_do_not_claim_azure_monitoring_is_not_enabled()
    {
        var repoRoot = FindRepoRoot();
        string[] relativePaths =
        [
            Path.Combine("src", "TrayApp", "SettingsWindow.xaml"),
            Path.Combine("src", "TrayApp", "SettingsWindow.xaml.cs"),
            Path.Combine("src", "TrayApp", "Services", "SettingsAzureAssociationService.cs"),
        ];

        foreach (var relative in relativePaths)
        {
            var path = Path.Combine(repoRoot, relative);
            Assert.True(File.Exists(path), $"Missing source file: {path}");
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("not enabled yet", text, StringComparison.OrdinalIgnoreCase);
        }
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
