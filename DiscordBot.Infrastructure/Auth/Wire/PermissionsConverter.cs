using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiscordBot.Infrastructure.Auth.Wire;

/// <summary>Discord sends permission bits as a string (they can exceed what a JSON number holds safely); older responses used a number.</summary>
internal sealed class PermissionsConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String ? (long.TryParse(reader.GetString(), out var bits) ? bits : 0) : reader.GetInt64();
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}
