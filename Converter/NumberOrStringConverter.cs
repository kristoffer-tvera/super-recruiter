using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuperRecruiter.Converter;

public class NumberOrStringConverter : JsonConverter<string>
{
    public override string Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        // if (reader.TokenType == JsonTokenType.Number)
        // {
        //     return reader.GetDouble().ToString();
        // }
        if (reader.TokenType == JsonTokenType.Number)
        {
            return Math.Round(reader.GetDouble(), 2).ToString();
        }
        return reader.GetString() ?? string.Empty;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
