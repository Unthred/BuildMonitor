using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;

namespace BuildMonitor.Infrastructure.LocalBuild;

public sealed record OutputLockReleaseResult(
    int ProcessesStopped,
    IReadOnlyList<string> StoppedDescriptions,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> Skipped);

[SupportedOSPlatform("windows")]
public static class OutputLockReleaser
{
    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BuildMonitor.TrayApp",
        "BuildMonitor",
        "devenv",
        "Cursor",
        "Code",
        "rider64",
        "idea64",
        "explorer",
        "MSBuild",
        "ServiceHub.Host.dotnet.x64",
        "ServiceHub.Host.dotnet.x86",
        "vbcscompiler",
        "csc",
        "dotnet",
        "powershell",
        "pwsh",
        "cmd",
        "conhost",
        "Windowsterminal"
    };

    private static readonly HashSet<string> ProtectedParentProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "devenv",
        "Cursor",
        "Code",
        "rider64",
        "idea64",
        "BuildMonitor.TrayApp",
        "MSBuild"
    };

    private static readonly TimeSpan HandleReleaseDelay = TimeSpan.FromMilliseconds(750);

    public static async Task<OutputLockReleaseResult> ReleaseAsync(
        string rootFolder,
        string projectFile,
        CancellationToken cancellationToken = default)
    {
        var stopped = new List<string>();
        var failures = new List<string>();
        var skipped = new List<string>();

        try
        {
            if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
            {
                return new OutputLockReleaseResult(0, stopped, failures, skipped);
            }

            var projectRoot = Path.GetFullPath(rootFolder);
            var assemblyName = Path.GetFileNameWithoutExtension(
                string.IsNullOrWhiteSpace(projectFile)
                    ? "App"
                    : Path.GetFileName(projectFile));

            var processDetails = QueryProcessDetails();
            var candidateIds = FindCandidateProcessIds(projectRoot, assemblyName, processDetails, skipped);

            foreach (var processId in candidateIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Process? process = null;
                try
                {
                    process = Process.GetProcessById(processId);
                    if (process.HasExited || process.Id == Environment.ProcessId)
                    {
                        continue;
                    }

                    if (IsProtectedParent(processId, processDetails))
                    {
                        skipped.Add(DescribeSkipped(process, "protected parent process"));
                        continue;
                    }

                    var description = DescribeProcess(process);
                    var stopResult = await ProcessTerminationHelper.TryStopGracefullyAsync(
                        process,
                        cancellationToken: cancellationToken);

                    if (stopResult.Success)
                    {
                        stopped.Add(description);
                    }
                    else
                    {
                        failures.Add($"{description}: {stopResult.Error ?? "Could not stop process"}");
                    }
                }
                catch (ArgumentException)
                {
                    // process exited between discovery and stop
                }
                catch (Exception ex)
                {
                    failures.Add($"PID {processId}: {DescribeException(ex)}");
                }
                finally
                {
                    process?.Dispose();
                }
            }

            if (stopped.Count > 0)
            {
                await Task.Delay(HandleReleaseDelay, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add($"Lock release failed: {DescribeException(ex)}");
        }

        return new OutputLockReleaseResult(stopped.Count, stopped, failures, skipped);
    }

    private static HashSet<int> FindCandidateProcessIds(
        string projectRoot,
        string assemblyName,
        IReadOnlyDictionary<int, ProcessDetails> processDetails,
        List<string> skipped)
    {
        var candidates = new HashSet<int>();
        var binRoot = Path.Combine(projectRoot, "bin");
        var objRoot = Path.Combine(projectRoot, "obj");
        var assemblyLower = assemblyName.ToLowerInvariant();
        var rootLower = projectRoot.ToLowerInvariant();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                using (process)
                {
                    if (process.HasExited || process.Id == Environment.ProcessId)
                    {
                        continue;
                    }

                    if (ProtectedProcessNames.Contains(process.ProcessName))
                    {
                        continue;
                    }

                    if (MatchesByExecutablePath(process, projectRoot, binRoot, objRoot, assemblyLower))
                    {
                        candidates.Add(process.Id);
                        continue;
                    }

                    if (MatchesByProcessName(process, assemblyName))
                    {
                        candidates.Add(process.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Skipped process during lock scan: {DescribeException(ex)}");
            }
        }

        foreach (var (processId, details) in processDetails)
        {
            if (processId == Environment.ProcessId)
            {
                continue;
            }

            if (IsProtectedHostProcess(details.CommandLine))
            {
                skipped.Add($"PID {processId}: protected host command line");
                continue;
            }

            if (IsProtectedParent(processId, processDetails))
            {
                skipped.Add($"PID {processId}: launched by protected parent");
                continue;
            }

            var normalized = details.CommandLine.ToLowerInvariant();
            if (normalized.Contains(rootLower, StringComparison.Ordinal)
                && (normalized.Contains($"{assemblyLower}.dll", StringComparison.Ordinal)
                    || normalized.Contains($"{assemblyLower}.exe", StringComparison.Ordinal)
                    || normalized.Contains("testhost", StringComparison.Ordinal)))
            {
                candidates.Add(processId);
            }
        }

        return candidates;
    }

    private sealed record ProcessDetails(string ProcessName, string CommandLine, int? ParentProcessId);

    private static Dictionary<int, ProcessDetails> QueryProcessDetails()
    {
        var results = new Dictionary<int, ProcessDetails>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, Name, CommandLine, ParentProcessId FROM Win32_Process");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var processId = Convert.ToInt32(item["ProcessId"]);
                var processName = item["Name"]?.ToString() ?? string.Empty;
                var commandLine = item["CommandLine"]?.ToString() ?? string.Empty;
                int? parentProcessId = item["ParentProcessId"] is null
                    ? null
                    : Convert.ToInt32(item["ParentProcessId"]);
                results[processId] = new ProcessDetails(processName, commandLine, parentProcessId);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Process query failed: {DescribeException(ex)}");
        }

        return results;
    }

    private static bool IsProtectedParent(int processId, IReadOnlyDictionary<int, ProcessDetails> processDetails)
    {
        if (!processDetails.TryGetValue(processId, out var details) || details.ParentProcessId is null)
        {
            return false;
        }

        if (!processDetails.TryGetValue(details.ParentProcessId.Value, out var parent))
        {
            return false;
        }

        var parentName = Path.GetFileNameWithoutExtension(parent.ProcessName);
        return ProtectedParentProcessNames.Contains(parentName);
    }

    private static bool MatchesByExecutablePath(
        Process process,
        string projectRoot,
        string binRoot,
        string objRoot,
        string assemblyLower)
    {
        string? executablePath;
        try
        {
            executablePath = process.MainModule?.FileName;
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(executablePath);
        if (IsUnderDirectory(fullPath, binRoot) || IsUnderDirectory(fullPath, objRoot))
        {
            return true;
        }

        return IsUnderDirectory(fullPath, projectRoot)
               && Path.GetFileNameWithoutExtension(fullPath).Equals(assemblyLower, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesByProcessName(Process process, string assemblyName) =>
        process.ProcessName.Equals(assemblyName, StringComparison.OrdinalIgnoreCase)
        || process.ProcessName.Equals(assemblyName + ".exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsProtectedHostProcess(string commandLine) =>
        commandLine.Contains("BuildMonitor.TrayApp", StringComparison.OrdinalIgnoreCase)
        || commandLine.Contains("BuildMonitor\\", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderDirectory(string filePath, string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return false;
        }

        var normalizedFile = Path.GetFullPath(filePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedDirectory = Path.GetFullPath(directoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return normalizedFile.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeProcess(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            return string.IsNullOrWhiteSpace(path)
                ? $"{process.ProcessName} (PID {process.Id})"
                : $"{process.ProcessName} (PID {process.Id}) — {path}";
        }
        catch
        {
            return $"{process.ProcessName} (PID {process.Id})";
        }
    }

    private static string DescribeSkipped(Process process, string reason) =>
        $"{DescribeProcess(process)} — {reason}";

    private static string DescribeException(Exception ex) =>
        ex switch
        {
            Win32Exception win32 => string.IsNullOrWhiteSpace(win32.Message)
                ? $"Win32 error {win32.NativeErrorCode}"
                : $"{win32.Message} (Win32 {win32.NativeErrorCode})",
            UnauthorizedAccessException => "Access is denied",
            _ => string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message
        };

    public static bool IsAccessDeniedFailure(string message) =>
        message.Contains("access denied", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
        || message.Contains("(Win32 5)", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Win32 error 5", StringComparison.OrdinalIgnoreCase);
}
