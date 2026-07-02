using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class BuildIssueCountResolverTests
{
    private const string FullLog = """
        C:\app\Pages\Index.razor(1,1): warning CS8618: Field required [C:\app\app.csproj]

        Build succeeded.
            1066 Warning(s)
            0 Error(s)
        """;

    private const string IncrementalLog = """
        WitherbyConnect -> C:\app\bin\Debug\net9.0\WitherbyConnect.dll

        Build succeeded.
            0 Warning(s)
            0 Error(s)
        """;

    [Fact]
    public void Resolve_incremental_output_uses_previous_log_counts()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bm-resolver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "last-build.log");
        var prevPath = logPath + ".prev";

        try
        {
            File.WriteAllText(prevPath, FullLog);

            var (errors, warnings) = BuildIssueCountResolver.Resolve(IncrementalLog, logPath);

            Assert.Equal(0, errors);
            Assert.Equal(1066, warnings);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
