using DiscordBot.Application.Models;

namespace DiscordBot.Application.Interfaces;

/// <summary>What is known of the Discord servers the bot is in. Each lookup gives null when the bot is not in that server.</summary>
public interface IGuildDirectory
{
    Task<GuildInfo?> FindAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>The server's text channels, in the order Discord shows them.</summary>
    Task<IReadOnlyList<GuildChannel>?> GetTextChannelsAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Whether the user is in the server. False too when the bot is not in the server, since nobody can be confirmed then.</summary>
    Task<bool> IsMemberAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the server shows for each of these users: their nickname there, else their global display name, else their username.
    /// Users who are not in the server (or when the bot is not in it) are left out, so the caller can fall back to the id.
    /// </summary>
    Task<IReadOnlyDictionary<ulong, string>> GetMemberNamesAsync(ulong guildId, IEnumerable<ulong> userIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GuildMember>?> SearchMembersAsync(ulong guildId, string? search, int limit, CancellationToken cancellationToken = default);

    /// <summary>The ids of the people who hold at least one of these roles, bots left out. Null when the bot is not in the server.</summary>
    Task<IReadOnlyCollection<ulong>?> GetRoleMemberIdsAsync(ulong guildId, IReadOnlyCollection<ulong> roleIds, CancellationToken cancellationToken = default);

    /// <summary>The roles that can sensibly be pinged: not @everyone, and not the roles Discord manages for bots and integrations.</summary>
    Task<IReadOnlyList<GuildRole>?> GetPingableRolesAsync(ulong guildId, CancellationToken cancellationToken = default);
}
