using System.Collections;
using System.Diagnostics;
using System.Text;

namespace BuildMonitor.Infrastructure.LocalBuild;

/// <summary>
/// Configures child dotnet processes launched from the hosted tray app.
///
/// Root problem: the tray app is started via "dotnet watch run" (SDK 10).
/// That pollutes PATH and DOTNET_* variables so child builds pick up the wrong
/// SDK/assemblies even when the project folder has a global.json pinning SDK 9.
/// </summary>
public static class DotNetProcessConfigurator
{
    private static readonly string HostApplicationDirectory =
        Path.GetFullPath(AppContext.BaseDirectory);

    private static readonly string DefaultDotNetRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "dotnet");

    private static readonly string[] VariablesToRemove =
    [
        "DOTNET_STARTUP_HOOKS",
        "DOTNET_ADDITIONAL_DEPS",
        "DOTNET_HOST_PATH",
        "DOTNET_HOTRELOAD_NAMEDPIPE_NAME",
        "DOTNET_MODIFIABLE_ASSEMBLIES",
        "DOTNET_WATCH",
        "DOTNET_WATCH_ITERATION",
        "DOTNET_WATCH_HOTRELOAD_METADATA_UPDATER",
        "DOTNET_WATCH_ITERATION_ID",
        "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES",
        "ASPNETCORE_AUTO_RELOAD_WS_ENDPOINT",
        "ASPNETCORE_AUTO_RELOAD_WS_KEY",
        "MSBUILDNODEHANDSHAKE",
        "MSBUILD_EXE_PATH",
        "MSBuildSDKsPath",
        "MSBUILDSDKSPATH",
    ];

    private static string? cachedDotNetPath;

    public static string ResolveDotNetExecutable()
    {
        if (cachedDotNetPath is not null && File.Exists(cachedDotNetPath))
        {
            return cachedDotNetPath;
        }

        var programFilesDotNet = Path.Combine(DefaultDotNetRoot, "dotnet.exe");
        if (File.Exists(programFilesDotNet))
        {
            cachedDotNetPath = programFilesDotNet;
            return cachedDotNetPath;
        }

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            var fromRoot = Path.Combine(dotnetRoot, "dotnet.exe");
            if (File.Exists(fromRoot))
            {
                cachedDotNetPath = fromRoot;
                return cachedDotNetPath;
            }
        }

        cachedDotNetPath = "dotnet";
        return cachedDotNetPath;
    }

    public static void Apply(ProcessStartInfo startInfo, IReadOnlyList<string> dotnetArguments) =>
        Apply(startInfo, dotnetArguments, forLongRunningHost: false);

    public static void Apply(
        ProcessStartInfo startInfo,
        IReadOnlyList<string> dotnetArguments,
        bool forLongRunningHost)
    {
        startInfo.FileName = ResolveDotNetExecutable();
        startInfo.UseShellExecute = false;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is not null)
            {
                startInfo.Environment[key] = entry.Value.ToString() ?? string.Empty;
            }
        }

        foreach (var key in VariablesToRemove)
        {
            startInfo.Environment.Remove(key);
        }

        // Let global.json in the project's working directory select the SDK.
        startInfo.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "1";
        startInfo.Environment["DOTNET_ROOT"] = DefaultDotNetRoot;

        if (startInfo.Environment.TryGetValue("PATH", out var path))
        {
            startInfo.Environment["PATH"] = BuildSanitizedPath(path ?? string.Empty, DefaultDotNetRoot);
        }

        if (!forLongRunningHost)
        {
            // Isolated one-shot builds only — watch/run should reuse MSBuild like the CLI.
            startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            startInfo.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        }

        startInfo.Environment["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "1";
        // Auto-restart on rude edits when dotnet watch cannot prompt (no console stdin).
        startInfo.Environment["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"] = "1";

        startInfo.ArgumentList.Clear();
        foreach (var arg in dotnetArguments)
        {
            startInfo.ArgumentList.Add(arg);
        }
    }

    private static string BuildSanitizedPath(string rawPath, string dotnetDir)
    {
        var segments = rawPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var kept = new List<string>(segments.Length + 1);

        foreach (var segment in segments)
        {
            if (ShouldRemovePathSegment(segment))
            {
                continue;
            }

            kept.Add(segment);
        }

        if (!kept.Any(p => string.Equals(p.TrimEnd('\\', '/'), dotnetDir, StringComparison.OrdinalIgnoreCase)))
        {
            kept.Insert(0, dotnetDir);
        }

        return string.Join(';', kept);
    }

    private static bool ShouldRemovePathSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return true;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(segment);
        }
        catch
        {
            return false;
        }

        if (fullPath.StartsWith(HostApplicationDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fullPath.Contains("BuildMonitor", StringComparison.OrdinalIgnoreCase)
            && (fullPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || fullPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }
}
