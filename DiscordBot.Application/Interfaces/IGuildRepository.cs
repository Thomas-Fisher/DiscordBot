using DiscordBot.Domain.Entities;

namespace DiscordBot.Application.Interfaces;

public interface IGuildRepository
{
    /// <summary>Adds the guild, or refreshes the name of one already stored (the original creation time is kept).</summary>
    Task SaveGuildAsync(Guild guild);

    /// <summary>Makes sure a row exists for the guild (events and message logs reference it). Does nothing if it already does.</summary>
    Task EnsureExistsAsync(ulong guildId);

    Task<IReadOnlyDictionary<string, string>> GetGuildSettingsAsync(ulong guildId);

    Task SaveSettingAsync(GuildSettings setting);
}
