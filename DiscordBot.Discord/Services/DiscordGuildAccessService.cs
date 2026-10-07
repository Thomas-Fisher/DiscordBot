using Discord;
using Discord.WebSocket;
using DiscordBot.Application;
using DiscordBot.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord.Services;

/// <summary>
/// Answers "can this user manage this guild?" from the bot's own view of the guild: the owner, or anyone with
/// Administrator / Manage Server. Results are cached briefly so API calls don't each hit Discord.
/// </summary>
public class DiscordGuildAccessService : IGuildAccessService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    private readonly DiscordSocketClient _client;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DiscordGuildAccessService> _logger;

    public DiscordGuildAccessService(DiscordSocketClient client, IMemoryCache cache, ILogger<DiscordGuildAccessService> logger)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
    }

    public bool IsAvailable => _client.ConnectionState == ConnectionState.Connected;

    public async Task<bool> CanManageGuildAsync(ulong userId, ulong guildId)
    {
        var cacheKey = $"guild-access:{guildId}:{userId}";

        if (_cache.TryGetValue(cacheKey, out bool cached))
        {
            return cached;
        }

        var guild = _client.GetGuild(guildId);
        if (guild == null)
        {
            // The bot isn't in this guild (or isn't connected yet): deny, but don't cache so it recovers on reconnect
            return false;
        }

        try
        {
            var allowed = await CheckAsync(guild, userId);
            _cache.Set(cacheKey, allowed, CacheDuration);
            return allowed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Transient Discord failure: deny this request without caching the answer
            _logger.LogWarning(ex, "Could not check guild access for user {UserId} in guild {GuildId}", userId, guildId);
            return false;
        }
    }

    public Task<IReadOnlyList<ulong>> GetManageableGuildIdsAsync(ulong userId)
    {
        // Members are all downloaded and kept current by the gateway, so this needs no Discord calls however many servers the bot is in
        var guildIds = _client.Guilds
            .Where(guild => guild.OwnerId == userId || (guild.GetUser(userId) is { } member && CanManage(guild, member)))
            .Select(guild => guild.Id)
            .ToList();

        return Task.FromResult<IReadOnlyList<ulong>>(guildIds);
    }

    private static async Task<bool> CheckAsync(SocketGuild guild, ulong userId)
    {
        if (guild.OwnerId == userId)
        {
            return true;
        }

        // The cache has everyone; the lookup is only for someone who joined moments ago
        var member = guild.GetUser(userId) ?? await ((IGuild)guild).GetUserAsync(userId);

        return member != null && CanManage(guild, member);
    }

    private static bool CanManage(SocketGuild guild, IGuildUser member)
    {
        return GuildAccessRules.CanManage(guild.OwnerId == member.Id, member.GuildPermissions.Administrator, member.GuildPermissions.ManageGuild);
    }
}
