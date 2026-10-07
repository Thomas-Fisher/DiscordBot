namespace DiscordBot.Application.Interfaces;

public interface IGuildAccessService
{
    /// <summary>False while the bot is not connected to Discord, when nobody's access can be decided (callers should say "try again", not "forbidden").</summary>
    bool IsAvailable { get; }

    Task<bool> CanManageGuildAsync(ulong userId, ulong guildId);

    Task<IReadOnlyList<ulong>> GetManageableGuildIdsAsync(ulong userId);
}
