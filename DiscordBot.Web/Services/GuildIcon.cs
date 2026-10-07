namespace DiscordBot.Web.Services;

public static class GuildIcon
{
    public static string Url(string guildId, string iconId)
    {
        return $"https://cdn.discordapp.com/icons/{guildId}/{iconId}.png";
    }
}
