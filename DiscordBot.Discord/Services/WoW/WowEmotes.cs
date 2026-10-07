using DiscordBot.Domain.Wow;

namespace DiscordBot.Discord.Services.WoW;

public static class WowEmotes
{
    private static IReadOnlyDictionary<string, ulong> _emoteIds = new Dictionary<string, ulong>();

    public static void Register(IReadOnlyDictionary<string, ulong> emoteIds)
    {
        Volatile.Write(ref _emoteIds, emoteIds);
    }

    public static string GetClassEmoteName(string className)
    {
        var lowerClass = className.ToLowerInvariant();
        if (Volatile.Read(ref _emoteIds).TryGetValue(lowerClass, out var emoteId))
        {
            return $"<:{lowerClass}:{emoteId}>";
        }
        return className;
    }

    public static string GetSpecEmoteName(string className, string specName)
    {
        var emoteName = $"{className.ToLowerInvariant()}_{specName.ToLowerInvariant()}";
        if (Volatile.Read(ref _emoteIds).TryGetValue(emoteName, out var emoteId))
        {
            return $"<:{emoteName}:{emoteId}>";
        }
        return emoteName;
    }

    public static IReadOnlyDictionary<string, string> IconUrls()
    {
        return Volatile.Read(ref _emoteIds).ToDictionary(pair => pair.Key, pair => $"https://cdn.discordapp.com/emojis/{pair.Value}.png");
    }

    public static bool IsWowClassEmote(string emote)
    {
        var name = EmoteName(emote);

        return WowCatalog.Classes.Any(c => name == c || name.StartsWith(c + "_", StringComparison.Ordinal));
    }

    public static string GetClassNameFromEmote(string emote)
    {
        var name = EmoteName(emote);

        return WowCatalog.Classes.Contains(name) ? name : "";
    }

    private static string EmoteName(string emote)
    {
        var name = emote.ToLowerInvariant();

        if (name.StartsWith("<:", StringComparison.Ordinal) && name.EndsWith('>'))
        {
            name = name.Trim('<', '>', ':').Split(':')[0];
        }

        return name;
    }
}
