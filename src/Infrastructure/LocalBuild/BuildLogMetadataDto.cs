using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.LocalBuild;

internal sealed class BuildLogMetadataDto
{
    public string ProjectId { get; set; } = string.Empty;
    public BuildLogKind Kind { get; set; }
    public string CommandLine { get; set; } = string.Empty;
    public int ExitCode { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset FinishedAtUtc { get; set; }
    public string LogFilePath { get; set; } = string.Empty;
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public List<string> ErrorLines { get; set; } = [];

    public BuildLogRecord ToRecord() => new(
        ProjectId,
        Kind,
        CommandLine,
        ExitCode,
        StartedAtUtc,
        FinishedAtUtc,
        LogFilePath,
        ErrorCount,
        ErrorLines,
        WarningCount);
}
