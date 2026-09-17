namespace BuildMonitor.Core.Rules;

public enum VerificationOperation
{
    Rebuild = 0,
    Tests = 1,
    ShipCheck = 2,
    Status = 3
}

/// <summary>Exclusive ownership and final-verification selection once a provider has claimed the worktree.</summary>
public static class VerificationProviderSession
{
    public static VerificationOperation FinalVerificationOperation => VerificationOperation.ShipCheck;

    public static bool AllowsDirectDotNet(bool claimed) => !claimed;

    public static string CommandName(VerificationOperation operation) => operation switch
    {
        VerificationOperation.Rebuild => "/run/rebuild",
        VerificationOperation.Tests => "/run/tests",
        VerificationOperation.ShipCheck => "/run/ship-check",
        VerificationOperation.Status => "GET /projects",
        _ => operation.ToString()
    };
}
