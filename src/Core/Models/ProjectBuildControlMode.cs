namespace BuildMonitor.Core.Models;

/// <summary>Who owns automatic rebuilds from file changes for a project.</summary>
public enum ProjectBuildControlMode
{
    /// <summary>Human development: file changes may schedule debounced auto-builds.</summary>
    FileWatching = 0,
    /// <summary>Agent ownership: file watcher observes only; builds require explicit API/UI.</summary>
    AiControlled = 1
}

public static class ProjectBuildControlModeWire
{
    public const string FileWatching = "file-watching";
    public const string AiControlled = "ai-controlled";

    public static string ToWire(ProjectBuildControlMode mode) =>
        mode switch
        {
            ProjectBuildControlMode.AiControlled => AiControlled,
            _ => FileWatching
        };

    public static bool TryParse(string? value, out ProjectBuildControlMode mode)
    {
        mode = ProjectBuildControlMode.FileWatching;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case FileWatching:
            case "filewatching":
            case "file_watching":
                mode = ProjectBuildControlMode.FileWatching;
                return true;
            case AiControlled:
            case "aicontrolled":
            case "ai_controlled":
                mode = ProjectBuildControlMode.AiControlled;
                return true;
            default:
                return false;
        }
    }

    public static string ToDisplayLabel(ProjectBuildControlMode mode) =>
        mode switch
        {
            ProjectBuildControlMode.AiControlled => "AI Controlled",
            _ => "File Watching"
        };
}
