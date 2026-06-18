using System.Diagnostics;

namespace BuildMonitor.Infrastructure.LocalBuild;

public sealed record BuildOutputRepairResult(
    IReadOnlyList<string> RemovedFolders,
    IReadOnlyList<string> Failures)
{
    public bool Repaired => RemovedFolders.Count > 0 && Failures.Count == 0;
    public bool Attempted => RemovedFolders.Count > 0 || Failures.Count > 0;
}

public static class BuildOutputTreeRepairer
{
    private static readonly string[] OutputFolderNames = ["artifacts", "bin", "obj"];

    public static BuildOutputRepairResult Repair(string projectRoot)
    {
        var removed = new List<string>();
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            failures.Add("Project root folder does not exist.");
            return new BuildOutputRepairResult(removed, failures);
        }

        var fullRoot = Path.GetFullPath(projectRoot);
        foreach (var folderName in OutputFolderNames)
        {
            var folderPath = Path.Combine(fullRoot, folderName);
            if (!Directory.Exists(folderPath))
            {
                continue;
            }

            try
            {
                DeleteDirectory(folderPath);
                removed.Add(folderName + "/");
            }
            catch (Exception ex)
            {
                failures.Add($"{folderName}/: {ex.Message}");
            }
        }

        return new BuildOutputRepairResult(removed, failures);
    }

    private static void DeleteDirectory(string directoryPath)
    {
        if (OperatingSystem.IsWindows())
        {
            DeleteDirectoryWindows(directoryPath);
            return;
        }

        Directory.Delete(directoryPath, recursive: true);
    }

    private static void DeleteDirectoryWindows(string directoryPath)
    {
        var fullPath = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var extendedPath = fullPath.StartsWith(@"\\?\", StringComparison.Ordinal)
            ? fullPath
            : @"\\?\" + fullPath;

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c rmdir /s /q \"{extendedPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start directory cleanup process.");

        process.WaitForExit();
        if (process.ExitCode != 0 && Directory.Exists(directoryPath))
        {
            throw new IOException($"Could not remove '{directoryPath}' (exit code {process.ExitCode}).");
        }
    }
}
