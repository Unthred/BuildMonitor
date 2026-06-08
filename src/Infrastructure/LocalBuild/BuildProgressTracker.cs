using System.Text.RegularExpressions;
using BuildMonitor.Core.Models;

namespace BuildMonitor.Infrastructure.LocalBuild;

public sealed class BuildProgressTracker
{
    private static readonly Regex ProjectBuiltRegex = new(
        @"^\s*(.+?)\s+->\s+",
        RegexOptions.Compiled);

    private static readonly Regex ErrorProjectPathRegex = new(
        @"(?<path>[^:\s\\]+(?:\\[^:\s\\]+)*\.csproj|\S+\.cs)\(\d+,\d+\):\s*error\s",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ErrorProjectFileRegex = new(
        @"(?<path>[^:\s\\]+(?:\\[^:\s\\]+)*\.csproj)\s*:\s*error\s",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly List<BuildProgressStep> steps = [];
    private const int RestoreIndex = 0;
    private bool restoreFinished;

    public IReadOnlyList<BuildProgressStep> Steps => steps;

    public void Reset()
    {
        steps.Clear();
        steps.Add(new BuildProgressStep("Restore packages", BuildStepStatus.Active));
        restoreFinished = false;
    }

    public bool OnOutputLine(string rawLine)
    {
        var line = StripAnsi(rawLine).Trim();
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var changed = false;

        if (!restoreFinished
            && (line.Contains("Determining projects to restore", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Restoring", StringComparison.OrdinalIgnoreCase)))
        {
            changed |= SetStepStatus(RestoreIndex, BuildStepStatus.Active);
        }

        if (!restoreFinished
            && (line.Contains("Restore succeeded", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Restore completed", StringComparison.OrdinalIgnoreCase)
                || line.Contains("All projects are up-to-date for restore", StringComparison.OrdinalIgnoreCase)))
        {
            restoreFinished = true;
            changed |= SetStepStatus(RestoreIndex, BuildStepStatus.Complete);
        }

        var builtMatch = ProjectBuiltRegex.Match(line);
        if (builtMatch.Success)
        {
            changed |= MarkProjectBuilt(builtMatch.Groups[1].Value.Trim());
        }

        if (line.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase))
        {
            changed |= FinalizeBuild(line.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase));
        }

        if (line.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            changed |= TryMarkFailedProjectFromErrorLine(line);
        }

        return changed;
    }

    public bool FinalizeFromResult(int exitCode, string output)
    {
        if (exitCode == 0)
        {
            return FinalizeBuild(failed: false);
        }

        var changed = FinalizeBuild(failed: true);
        if (steps.Any(s => s.Status == BuildStepStatus.Failed))
        {
            return changed;
        }

        foreach (var line in output.Replace("\r\n", "\n").Split('\n'))
        {
            if (TryMarkFailedProjectFromErrorLine(StripAnsi(line).Trim()))
            {
                changed = true;
            }
        }

        if (!steps.Any(s => s.Status == BuildStepStatus.Failed))
        {
            steps.Add(new BuildProgressStep("Build failed", BuildStepStatus.Failed));
            changed = true;
        }

        return changed;
    }

    private bool TryMarkFailedProjectFromErrorLine(string line)
    {
        var pathMatch = ErrorProjectPathRegex.Match(line);
        if (!pathMatch.Success)
        {
            pathMatch = ErrorProjectFileRegex.Match(line);
        }

        if (!pathMatch.Success)
        {
            return false;
        }

        var path = pathMatch.Groups["path"].Value.Trim().Trim('"');
        var projectName = path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            ? NormalizeName(path)
            : NormalizeName(Path.GetFileName(Path.GetDirectoryName(path) ?? path));

        return MarkProjectFailed(projectName);
    }

    private bool MarkProjectFailed(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return false;
        }

        if (!restoreFinished)
        {
            restoreFinished = true;
            SetStepStatus(RestoreIndex, BuildStepStatus.Complete);
        }

        var normalized = NormalizeName(projectName);
        var index = FindProjectStepIndex(normalized);
        if (index < 0)
        {
            steps.Add(new BuildProgressStep(normalized, BuildStepStatus.Failed));
            return true;
        }

        return SetStepStatus(index, BuildStepStatus.Failed);
    }

    private bool MarkProjectBuilt(string builtName)
    {
        if (!restoreFinished)
        {
            restoreFinished = true;
            SetStepStatus(RestoreIndex, BuildStepStatus.Complete);
        }

        var normalized = NormalizeName(builtName);
        var index = FindProjectStepIndex(normalized);
        if (index < 0)
        {
            steps.Add(new BuildProgressStep(normalized, BuildStepStatus.Complete));
            return true;
        }

        return SetStepStatus(index, BuildStepStatus.Complete);
    }

    private bool FinalizeBuild(bool failed)
    {
        var changed = false;
        if (!restoreFinished)
        {
            restoreFinished = true;
            changed |= SetStepStatus(
                RestoreIndex,
                failed ? BuildStepStatus.Failed : BuildStepStatus.Complete);
        }

        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].Status != BuildStepStatus.Active)
            {
                continue;
            }

            changed |= SetStepStatus(i, failed ? BuildStepStatus.Failed : BuildStepStatus.Complete);
        }

        return changed;
    }

    private int FindProjectStepIndex(string normalizedBuilt)
    {
        for (var i = 1; i < steps.Count; i++)
        {
            var label = NormalizeName(steps[i].Label);
            if (label.Equals(normalizedBuilt, StringComparison.OrdinalIgnoreCase)
                || normalizedBuilt.Contains(label, StringComparison.OrdinalIgnoreCase)
                || label.Contains(normalizedBuilt, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private bool SetStepStatus(int index, BuildStepStatus status)
    {
        if (index < 0 || index >= steps.Count || steps[index].Status == status)
        {
            return false;
        }

        steps[index] = steps[index] with { Status = status };
        return true;
    }

    private static string NormalizeName(string value) =>
        Path.GetFileNameWithoutExtension(value.Trim().Trim('"'));

    private static string StripAnsi(string line) =>
        Regex.Replace(line, @"\x1b\[[0-9;]*m", string.Empty);
}
