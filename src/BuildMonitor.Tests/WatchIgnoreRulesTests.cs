using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public class WatchIgnoreRulesTests
{
    [Theory]
    [InlineData(@"C:\proj\bin\Debug\app.dll", true)]
    [InlineData(@"C:\proj\.cursor\plans\foo.md", true)]
    [InlineData(@"C:\proj\src\App.cs", false)]
    [InlineData(@"C:\proj\logs\last-build.log", true)]
    [InlineData(@"C:\proj\src\Page.razor", false)]
    [InlineData(@"C:\proj\Thumbs.db", true)]
    [InlineData(@"C:\proj\wwwroot\Images\logo.png", true)]
    [InlineData(@"C:\proj\wwwroot\Files\guide.pdf", false)]
    public void ShouldIgnorePath_classifies_noise_and_source(string path, bool expected) =>
        Assert.Equal(
            expected,
            WatchIgnoreRules.ShouldIgnorePath(path, WatchExcludeSegments.DefaultSegments));

    [Fact]
    public void FilterMeaningfulPaths_removes_ignored_entries()
    {
        var filtered = WatchIgnoreRules.FilterMeaningfulPaths(
        [
            @"C:\proj\obj\foo.cache",
            @"C:\proj\src\Home.razor"
        ],
        WatchExcludeSegments.DefaultSegments);

        Assert.Single(filtered);
        Assert.Contains("Home.razor", filtered[0], StringComparison.Ordinal);
    }
}
