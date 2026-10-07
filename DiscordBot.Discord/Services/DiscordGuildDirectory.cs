using Discord;
using Discord.WebSocket;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;

namespace DiscordBot.Discord.Services;

public class DiscordGuildDirectory : IGuildDirectory
{
    private readonly DiscordSocketClient _client;

    public DiscordGuildDirectory(DiscordSocketClient client)
    {
        _client = client;
    }

    public Task<GuildInfo?> FindAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var guild = _client.GetGuild(guildId);

        return Task.FromResult(guild == null
            ? null
            : new GuildInfo(guild.Id, guild.Name, guild.IconId, guild.Description, guild.MemberCount, guild.CreatedAt.UtcDateTime,
                guild.OwnerId, (int)guild.PremiumTier, guild.Roles.Count, guild.Channels.Count));
    }

    public Task<IReadOnlyList<GuildChannel>?> GetTextChannelsAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GuildChannel>? channels = _client.GetGuild(guildId)?.Channels
            .Where(c => c is ITextChannel)
            .OrderBy(c => c.Position)
            .Select(c => new GuildChannel(c.Id, c.Name, c.Position))
            .ToList();

        return Task.FromResult(channels);
    }

    public Task<bool> IsMemberAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_client.GetGuild(guildId)?.GetUser(userId) != null);
    }

    public Task<IReadOnlyDictionary<ulong, string>> GetMemberNamesAsync(ulong guildId, IEnumerable<ulong> userIds, CancellationToken cancellationToken = default)
    {
        var names = new Dictionary<ulong, string>();
        var guild = _client.GetGuild(guildId);
        if (guild != null)
        {
            foreach (var userId in userIds.Distinct())
            {
                if (guild.GetUser(userId) is { } member)
                {
                    names[userId] = member.DisplayName;
                }
            }
        }

        return Task.FromResult<IReadOnlyDictionary<ulong, string>>(names);
    }

    public Task<IReadOnlyList<GuildMember>?> SearchMembersAsync(ulong guildId, string? search, int limit, CancellationToken cancellationToken = default)
    {
        var term = search?.Trim();

        IReadOnlyList<GuildMember>? members = _client.GetGuild(guildId)?.Users
            .Where(user => !user.IsBot
                && (string.IsNullOrEmpty(term)
                    || user.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || user.Username.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(user => new GuildMember(user.Id, user.DisplayName, user.Username, user.GetDisplayAvatarUrl(size: 64)))
            .ToList();

        return Task.FromResult(members);
    }

    public Task<IReadOnlyCollection<ulong>?> GetRoleMemberIdsAsync(ulong guildId, IReadOnlyCollection<ulong> roleIds, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ulong>? members = _client.GetGuild(guildId)?.Users
            .Where(user => !user.IsBot && user.Roles.Any(role => roleIds.Contains(role.Id)))
            .Select(user => user.Id)
            .ToList();

        return Task.FromResult(members);
    }

    public Task<IReadOnlyList<GuildRole>?> GetPingableRolesAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GuildRole>? roles = _client.GetGuild(guildId)?.Roles
            .Where(r => !r.IsEveryone && !r.IsManaged)
            .OrderByDescending(r => r.Position)
            .Select(r => new GuildRole(r.Id, r.Name, r.Position))
            .ToList();

        return Task.FromResult(roles);
    }
}
