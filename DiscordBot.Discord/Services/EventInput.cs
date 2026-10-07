using System.Globalization;
using DiscordBot.Discord.Constants;
using DiscordBot.Domain.Common;
using DiscordBot.Application.Errors;

namespace DiscordBot.Discord.Services;

public static class EventInput
{
    /// <summary>
    /// Parses the "DD-MM-YYYY HH:MM" text people enter, which is the clock time in the guild's time zone, into UTC.
    /// </summary>
    public static bool TryParseDateTime(string? text, GuildTimeZone zone, out DateTime utc)
    {
        // No style flags: the text is a plain clock reading, and the zone (not this machine's) says which instant it means
        if (DateTime.TryParseExact(text, DateTimeFormats.EventInputFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            utc = zone.ToUtc(local);
            return true;
        }

        utc = default;
        return false;
    }

    public static string InvalidFormatMessage(GuildTimeZone zone)
    {
        return $"❌ Invalid date/time format. Use {DateTimeFormats.EventInputFormatHint} (time zone: {zone.Id}, change it with /timezone).";
    }

    public static string MessageFor(Error error)
    {
        return error == EventErrors.NothingToChange ? $"ℹ️ {error.Message}" : $"❌ {error.Message}";
    }
}
