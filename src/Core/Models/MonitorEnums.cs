namespace BuildMonitor.Core.Models;

public enum MonitorHealth
{
    Unknown = 0,
    Green = 1,
    Amber = 2,
    Red = 3
}

public enum PipelineRunState
{
    Unknown = 0,
    NotStarted = 1,
    InProgress = 2,
    Completed = 3,
    Canceling = 4
}

public enum PipelineRunResult
{
    Unknown = 0,
    Succeeded = 1,
    PartiallySucceeded = 2,
    Failed = 3,
    Canceled = 4
}

public enum NotificationMode
{
    FailuresOnly = 0,
    FailuresAndRecovery = 1,
    AllStateChanges = 2
}
