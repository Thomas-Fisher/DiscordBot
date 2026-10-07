using DiscordBot.Application.Activity;

namespace DiscordBot.Discord.Presentation;

public sealed class DiscordActivityText : IActivityText
{
    private readonly ulong _guildId;

    public DiscordActivityText(ulong guildId)
    {
        _guildId = guildId;
    }

    public string User(ulong id)
    {
        return $"<@{id}>";
    }

    public string Channel(ulong? id, string? name)
    {
        return id is { } channelId ? $"<#{channelId}>" : name == null ? "a channel" : $"#{Escape(name)}";
    }

    public string Verb(string text)
    {
        return $"**{text}**";
    }

    public string Name(string text)
    {
        return $"*{Escape(text)}*";
    }

    public string Quote(string text)
    {
        return $"\"{Escape(text)}\"";
    }

    public string Emoji(string raw, string? name)
    {
        return raw;
    }

    public string Time(DateTime utc)
    {
        return DiscordTimestamp.ShortDateTime(utc);
    }

    public string Jump(ulong channelId, ulong messageId)
    {
        return $" ([jump](https://discord.com/channels/{_guildId}/{channelId}/{messageId}))";
    }

    /// <summary>Stops text typed by someone else from formatting the line (bold, spoilers, code) or turning into links.</summary>
    public static string Escape(string text)
    {
        var escaped = new System.Text.StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character is '\\' or '*' or '_' or '~' or '`' or '|' or '>' or '[' or ']' or '<')
            {
                escaped.Append('\\');
            }

            escaped.Append(character);
        }

        return escaped.ToString();
    }
}
