using System.Globalization;
using DiscordBot.Domain.Common;

namespace DiscordBot.Application.Activity;

public sealed class PlainActivityText : IActivityText
{
    private readonly Func<ulong, string> _userName;
    private readonly GuildTimeZone _zone;

    public PlainActivityText(Func<ulong, string> userName, GuildTimeZone zone)
    {
        _userName = userName;
        _zone = zone;
    }

    public string User(ulong id)
    {
        return _userName(id);
    }

    public string Channel(ulong? id, string? name)
    {
        return name == null ? "a channel" : $"#{name}";
    }

    public string Verb(string text)
    {
        return text;
    }

    public string Name(string text)
    {
        return $"\"{text}\"";
    }

    public string Quote(string text)
    {
        return $"\"{text}\"";
    }

    public string Emoji(string raw, string? name)
    {
        return name == null ? raw : $":{name}:";
    }

    public string Time(DateTime utc)
    {
        return _zone.ToLocal(utc).ToString("MMM d, HH:mm", CultureInfo.InvariantCulture);
    }

    public string Jump(ulong channelId, ulong messageId)
    {
        return "";
    }
}
