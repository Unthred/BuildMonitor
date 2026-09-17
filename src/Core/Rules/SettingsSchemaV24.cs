namespace BuildMonitor.Core.Rules;

/// <summary>
/// Schema v24 adds optional per-project <c>local.applicationUrl</c> so runtime ports
/// persist in BuildMonitor settings instead of rewriting launchSettings.json.
/// </summary>
public static class SettingsSchemaV24
{
    public const int Version = 24;
}
