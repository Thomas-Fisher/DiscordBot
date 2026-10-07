using DiscordBot.Domain.Enums;

namespace DiscordBot.Discord.Constants;

public static class EventTypeOptions
{
    public static readonly IReadOnlyList<(string Label, string Value, string Description, EventType Type)> All = new[]
    {
        ("Generic", "generic", "A generic event", EventType.Generic),
        ("Wow Classic", "wow_classic", "World of Warcraft Classic", EventType.WowClassic),
        ("Wow Forever", "wow_forever", "World of Warcraft Forever", EventType.WowForever)
    };

    /// <summary>The type for a picked value; anything unrecognised is a generic event.</summary>
    public static EventType Parse(string? value)
    {
        foreach (var option in All)
        {
            if (option.Value == value)
            {
                return option.Type;
            }
        }

        return EventType.Generic;
    }
}
