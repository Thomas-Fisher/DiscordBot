using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiscordBot.Contracts;

/// <summary>
/// Writes every id as text: Discord ids are too big for a JavaScript number to hold exactly. Numbers are still accepted when reading.
/// </summary>
public sealed class DiscordIdConverter : JsonConverter<ulong>
{
    public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return ulong.TryParse(reader.GetString(), out var id) ? id : throw new JsonException("An id must be a whole number.");
        }

        return reader.GetUInt64();
    }

    public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
