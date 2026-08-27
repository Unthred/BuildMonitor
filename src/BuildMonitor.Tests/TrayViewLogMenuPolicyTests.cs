using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Tests;

public sealed class TrayViewLogMenuPolicyTests
{
    [Fact]
    public void Local_active_project_offers_view_log()
    {
        var project = LocalActive("Local App");
        Assert.True(TrayViewLogMenuPolicy.OffersLocalViewLog(project));
        Assert.Equal(
            [project],
            TrayViewLogMenuPolicy.SelectLocalLogProjects([project]));
    }

    [Fact]
    public void Azure_only_project_does_not_offer_local_view_log()
    {
        var azureOnly = new MonitoredProjectSettings
        {
            DisplayName = "Azure only",
            IsActiveInSession = true,
            Local = null,
            Azure = new AzureDevOpsProjectAttachment
            {
                ConnectionId = "conn",
                AdoProjectId = "proj",
                AdoProjectName = "Proj",
                RepositoryId = "r1",
                RepositoryName = "Repo"
            }
        };

        Assert.False(TrayViewLogMenuPolicy.OffersLocalViewLog(azureOnly));
        Assert.Empty(TrayViewLogMenuPolicy.SelectLocalLogProjects([azureOnly]));
    }

    [Fact]
    public void Inactive_local_project_excluded()
    {
        var project = LocalActive("Idle");
        project.IsActiveInSession = false;
        Assert.False(TrayViewLogMenuPolicy.OffersLocalViewLog(project));
        Assert.Empty(TrayViewLogMenuPolicy.SelectLocalLogProjects([project]));
    }

    [Fact]
    public void Select_keeps_only_local_active_among_mixed_list()
    {
        var local = LocalActive("With local");
        var azureOnly = new MonitoredProjectSettings
        {
            DisplayName = "Azure",
            IsActiveInSession = true,
            Azure = new AzureDevOpsProjectAttachment
            {
                ConnectionId = "c",
                AdoProjectId = "p",
                AdoProjectName = "P",
                RepositoryId = "r",
                RepositoryName = "R"
            }
        };
        var inactive = LocalActive("Off");
        inactive.IsActiveInSession = false;

        var selected = TrayViewLogMenuPolicy.SelectLocalLogProjects([azureOnly, local, inactive]);
        Assert.Equal([local], selected);
    }

    [Fact]
    public void Menu_labels_are_view_log_for_both_layout_modes()
    {
        Assert.Equal("View log", TrayViewLogMenuPolicy.ItemText);
        Assert.Equal("View log", TrayViewLogMenuPolicy.ByOperationRootText);
    }

    private static MonitoredProjectSettings LocalActive(string name) => new()
    {
        DisplayName = name,
        IsActiveInSession = true,
        Local = new LocalProjectAttachment
        {
            RootFolder = @"C:\src\demo",
            ProjectFile = "Demo.csproj"
        }
    };
}

public sealed class LogViewerWindowReuseTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void ShouldActivateExisting_matches_open_and_loaded(
        bool hasOpenEntry,
        bool windowIsLoaded,
        bool expected) =>
        Assert.Equal(
            expected,
            LogViewerWindowReuse.ShouldActivateExisting(hasOpenEntry, windowIsLoaded));
}
