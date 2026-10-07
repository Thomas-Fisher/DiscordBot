using Discord.WebSocket;

namespace DiscordBot.Discord.Services;

public static class GuildMemberNames
{
    /// <summary>
    /// Looks up what a guild shows for a member: the nickname they set there, else their global display name, else their username.
    /// Null when the member is not in the guild (or not cached), so the caller can fall back to a mention.
    /// </summary>
    public static Func<ulong, string?> For(DiscordSocketClient client, ulong guildId)
    {
        return userId => client.GetGuild(guildId)?.GetUser(userId)?.DisplayName;
    }
}
