using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class HttpUriNavigationValidatorTests
{
    [Theory]
    [InlineData("https://dev.azure.com/org/project/_build/results?buildId=1")]
    [InlineData("http://localhost:5000")]
    public void TryParseAllowed_accepts_http_and_https(string uriText)
    {
        Assert.True(HttpUriNavigationValidator.TryParseAllowed(uriText, out var uri));
        Assert.NotNull(uri);
    }

    [Theory]
    [InlineData("file:///C:/temp/x.txt")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not-a-uri")]
    public void TryParseAllowed_rejects_non_http_schemes(string uriText)
    {
        Assert.False(HttpUriNavigationValidator.TryParseAllowed(uriText, out _));
    }
}
