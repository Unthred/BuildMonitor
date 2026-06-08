namespace BuildMonitor.TrayApp.Services;

public static class BuildTimestampFormatter
{
    public static string FormatLocal(DateTimeOffset utc) =>
        utc.ToLocalTime().ToString("ddd, dd MMM yyyy HH:mm:ss");

    public static string FormatLocalShort(DateTimeOffset utc) =>
        utc.ToLocalTime().ToString("g");
}
