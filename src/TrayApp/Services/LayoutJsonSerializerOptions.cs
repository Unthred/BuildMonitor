using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildMonitor.TrayApp.Services;

internal static class LayoutJsonSerializerOptions
{
    internal static JsonSerializerOptions Create() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonFiniteDoubleConverter() }
    };
}
