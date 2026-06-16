using BuildMonitor.Core.Models;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public class LogErrorExporterTests
{
    [Fact]
    public void ResolvePrimaryLogKind_uses_run_log_when_watching_with_run_errors()
    {
        var kind = LogErrorExporter.ResolvePrimaryLogKind(
            ProjectLifecycleState.Watching,
            "Run: 2 errors | 0 warnings");

        Assert.Equal(BuildLogKind.Run, kind);
    }

    [Fact]
    public void ResolvePrimaryLogKind_uses_build_log_for_build_failed()
    {
        var kind = LogErrorExporter.ResolvePrimaryLogKind(
            ProjectLifecycleState.BuildFailed,
            "Build: 3 errors | 0 warnings");

        Assert.Equal(BuildLogKind.Build, kind);
    }
}
