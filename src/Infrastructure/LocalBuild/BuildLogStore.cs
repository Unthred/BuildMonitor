using System.Text;
using System.Text.Json;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.LocalBuild;

public sealed class BuildLogStore(string logsRootDirectory)
{
    public string GetLogPath(string projectId, BuildLogKind kind)
    {
        var fileName = kind switch
        {
            BuildLogKind.Test => "last-test.log",
            BuildLogKind.WatchCompile => "last-watch.log",
            BuildLogKind.Run => "last-run.log",
            _ => "last-build.log"
        };

        return Path.Combine(logsRootDirectory, projectId, fileName);
    }

    public async Task<BuildLogRecord> SaveAsync(
        string projectId,
        BuildLogKind kind,
        string commandLine,
        int exitCode,
        DateTimeOffset startedAtUtc,
        string logText,
        CancellationToken cancellationToken = default)
    {
        var projectDir = Path.Combine(logsRootDirectory, projectId);
        Directory.CreateDirectory(projectDir);

        var fileName = kind switch
        {
            BuildLogKind.Test => "last-test.log",
            BuildLogKind.WatchCompile => "last-watch.log",
            BuildLogKind.Run => "last-run.log",
            _ => "last-build.log"
        };

        var logPath = Path.Combine(projectDir, fileName);
        var prevPath = logPath + ".prev";
        var priorPersisted = BuildIssueCountResolver.ReadPersistedMetadataCounts(logPath);
        if (File.Exists(logPath))
        {
            File.Copy(logPath, prevPath, overwrite: true);
        }

        await File.WriteAllTextAsync(logPath, logText, cancellationToken);

        var (resolvedErrors, resolvedWarnings) = BuildIssueCountResolver.Resolve(logText, logPath);
        var (parsedErrors, errorLines) = BuildLogParser.ParseErrors(logText);
        if (resolvedErrors == 0 && resolvedWarnings == 0
            && IncrementalBuildDetector.WasCompileSkipped(logText)
            && File.Exists(prevPath))
        {
            var prevText = await File.ReadAllTextAsync(prevPath, cancellationToken);
            (parsedErrors, errorLines) = BuildLogParser.ParseErrors(prevText);
            resolvedErrors = parsedErrors;
            resolvedWarnings = BuildLogParser.ParseWarningCount(prevText);
        }

        var errorCount = Math.Max(Math.Max(parsedErrors, resolvedErrors), priorPersisted.Errors);
        var warningCount = Math.Max(
            Math.Max(BuildLogParser.ParseWarningCount(logText), resolvedWarnings),
            priorPersisted.Warnings);
        var finishedAt = DateTimeOffset.UtcNow;
        var record = new BuildLogRecord(
            projectId,
            kind,
            commandLine,
            exitCode,
            startedAtUtc,
            finishedAt,
            logPath,
            errorCount,
            errorLines,
            warningCount);

        var metaPath = Path.Combine(projectDir, $"{Path.GetFileNameWithoutExtension(fileName)}.meta.json");
        var dto = new BuildLogMetadataDto
        {
            ProjectId = record.ProjectId,
            Kind = record.Kind,
            CommandLine = record.CommandLine,
            ExitCode = record.ExitCode,
            StartedAtUtc = record.StartedAtUtc,
            FinishedAtUtc = record.FinishedAtUtc,
            LogFilePath = record.LogFilePath,
            ErrorCount = record.ErrorCount,
            WarningCount = record.WarningCount,
            ErrorLines = record.ErrorLines.ToList()
        };
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(dto), cancellationToken);
        return record;
    }

    public async Task<BuildLogRecord?> LoadMetadataAsync(string projectId, BuildLogKind kind, CancellationToken cancellationToken = default)
    {
        var fileName = kind switch
        {
            BuildLogKind.Test => "last-test",
            BuildLogKind.WatchCompile => "last-watch",
            BuildLogKind.Run => "last-run",
            _ => "last-build"
        };

        var metaPath = Path.Combine(logsRootDirectory, projectId, $"{fileName}.meta.json");
        if (!File.Exists(metaPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(metaPath, cancellationToken);
        var dto = JsonSerializer.Deserialize<BuildLogMetadataDto>(json);
        return dto?.ToRecord();
    }

    public async Task<string> LoadLogTextAsync(BuildLogRecord record, int maxBytes, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(record.LogFilePath))
        {
            return string.Empty;
        }

        await using var stream = File.OpenRead(record.LogFilePath);
        if (stream.Length <= maxBytes)
        {
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(cancellationToken);
        }

        stream.Seek(-maxBytes, SeekOrigin.End);
        using var tailReader = new StreamReader(stream);
        var tail = await tailReader.ReadToEndAsync(cancellationToken);
        return $"... (truncated, showing last {maxBytes} bytes)\n{tail}";
    }

    public static string TruncateTailForDisplay(string text, int maxBytes)
    {
        if (string.IsNullOrEmpty(text) || maxBytes <= 0)
        {
            return text;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= maxBytes)
        {
            return text;
        }

        var start = bytes.Length - maxBytes;
        while (start < bytes.Length && (bytes[start] & 0xC0) == 0x80)
        {
            start++;
        }

        var tail = Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
        return $"... (truncated, showing last {maxBytes} bytes)\n{tail}";
    }
}

