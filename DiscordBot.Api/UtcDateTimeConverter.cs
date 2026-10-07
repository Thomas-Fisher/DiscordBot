using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiscordBot.Api;

/// <summary>
/// Times are UTC on the wire: an offset is converted, no offset is read as UTC (not as the server's local time), and output
/// always ends in <c>Z</c>.
/// </summary>
public class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();

        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            throw new JsonException($"'{text}' is not a valid date and time.");
        }

        return parsed.UtcDateTime;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // A time that arrives here unlabelled is the application's own UTC, never the server's local time
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        writer.WriteStringValue(utc);
    }
}
