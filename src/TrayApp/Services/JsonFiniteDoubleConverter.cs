using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildMonitor.TrayApp.Services;

internal sealed class JsonFiniteDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => double.NaN,
            JsonTokenType.Number => reader.GetDouble(),
            _ => double.NaN
        };

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        if (!double.IsFinite(value))
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteNumberValue(value);
    }
}
