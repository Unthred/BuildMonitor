namespace BuildMonitor.Core.Rules;

using BuildMonitor.Core.Models;

/// <summary>What a cold start knows about the project's last build and its sources.</summary>
/// <param name="LastBuildExitCode">Exit code of the saved last build, or null when there is none.</param>
/// <param name="LastBuildStartedAtUtc">When the saved last build started, or null when unknown.</param>
/// <param name="BuildOutputWrittenUtc">Last write of the startup project's built assembly, or null when it is missing.</param>
/// <param name="NewestSourceWriteUtc">Latest write of any meaningful source file, or null when none was found.</param>
public sealed record StartupBuildInputs(
    ProjectBuildControlMode BuildControlMode,
    int? LastBuildExitCode,
    DateTimeOffset? LastBuildStartedAtUtc,
    DateTimeOffset? BuildOutputWrittenUtc,
    DateTimeOffset? NewestSourceWriteUtc);

public enum StartupBuildDecisionKind
{
    Build,
    SkipUpToDate
}

public sealed record StartupBuildDecision(StartupBuildDecisionKind Kind, string Reason)
{
    public bool ShouldBuild => Kind == StartupBuildDecisionKind.Build;
}

/// <summary>
/// Decides whether a cold <c>StartOnLaunch</c> start compiles before hosting.
/// File Watching always builds. AI Controlled reuses the existing output when the last
/// build succeeded and no meaningful source file changed after both that build started
/// and the output assembly was written.
/// </summary>
public static class StartupBuildPolicy
{
    public static StartupBuildDecision Decide(StartupBuildInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.BuildControlMode != ProjectBuildControlMode.AiControlled)
        {
            return Build("File Watching builds on startup");
        }

        if (inputs.LastBuildExitCode is null || inputs.LastBuildStartedAtUtc is null)
        {
            return Build("no saved build");
        }

        if (inputs.LastBuildExitCode != 0)
        {
            return Build("last build failed");
        }

        if (inputs.BuildOutputWrittenUtc is null)
        {
            return Build("build output missing");
        }

        // Compare with the build's start (a file saved mid-build may be missing from it) and
        // with the output's write time (the last build may have been a Release ship-check
        // that left the Debug output the host runs from untouched). Use the earlier one.
        var freshAsOf = inputs.LastBuildStartedAtUtc.Value < inputs.BuildOutputWrittenUtc.Value
            ? inputs.LastBuildStartedAtUtc.Value
            : inputs.BuildOutputWrittenUtc.Value;

        if (inputs.NewestSourceWriteUtc is { } newest && newest >= freshAsOf)
        {
            return Build("sources changed since last build");
        }

        return new StartupBuildDecision(
            StartupBuildDecisionKind.SkipUpToDate,
            "no source changes since last successful build");
    }

    private static StartupBuildDecision Build(string reason) =>
        new(StartupBuildDecisionKind.Build, reason);
}
