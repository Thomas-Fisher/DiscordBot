namespace DiscordBot.Application.Activity;

/// <summary>How the pieces of an activity sentence are written for one place it is shown: mentions and bold in Discord, plain names on the dashboard.</summary>
public interface IActivityText
{
    /// <summary>A person: a mention in Discord, their name on the dashboard.</summary>
    string User(ulong id);

    /// <summary>A channel, by its id when it is known (a clickable channel in Discord) and otherwise by the name that was recorded.</summary>
    string Channel(ulong? id, string? name);

    /// <summary>The word that says what was done ("joined", "deleted"), emphasised where that is possible.</summary>
    string Verb(string text);

    /// <summary>An event title or a role name.</summary>
    string Name(string text);

    /// <summary>A short piece of someone's text, such as an edited message.</summary>
    string Quote(string text);

    /// <summary>A reaction: the emote itself in Discord, its name on the dashboard.</summary>
    string Emoji(string raw, string? name);

    string Time(DateTime utc);

    /// <summary>A link to a message, with the space or brackets it needs, or nothing where links cannot be shown.</summary>
    string Jump(ulong channelId, ulong messageId);
}
