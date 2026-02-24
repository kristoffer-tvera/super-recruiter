using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuperRecruiter.Shared.Converters;

public class DoubleOrDashConverter : JsonConverter<double>
{
    public override double Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetDouble();
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (stringValue == "-" || string.IsNullOrWhiteSpace(stringValue))
            {
                return 0.0;
            }

            if (double.TryParse(stringValue, out var result))
            {
                return result;
            }
        }

        return 0.0;
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}
