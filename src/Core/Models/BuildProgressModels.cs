namespace BuildMonitor.Core.Models;

public enum BuildStepStatus
{
    Pending = 0,
    Active = 1,
    Complete = 2,
    Failed = 3
}

public sealed record BuildProgressStep(string Label, BuildStepStatus Status);
