namespace BuildMonitor.Infrastructure.LocalBuild;

public static class WatchExcludeSnippetFormatter
{
    public static string FormatCsprojSnippet(IEnumerable<string> segments)
    {
        var lines = segments
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s => $"    <Watch Remove=\"**/{s.Trim('/')}/**\" />");

        return "<!-- Add inside your .csproj to reduce dotnet watch rebuilds from IDE folders -->\n"
            + "<ItemGroup>\n"
            + string.Join(Environment.NewLine, lines)
            + "\n</ItemGroup>";
    }
}
