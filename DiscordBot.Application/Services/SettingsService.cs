using System.Text.Json;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Common;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Application.Services;

/// <summary>
/// Reads and writes per-guild settings. All of a guild's settings are loaded in one query and cached briefly,
/// because they are read on the hot path (every message and every activity log). Saving invalidates the cache.
/// </summary>
public class SettingsService : ISettingsService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IGuildRepository _guilds;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(IGuildRepository guilds, IMemoryCache cache, ILogger<SettingsService> logger)
    {
        _guilds = guilds;
        _cache = cache;
        _logger = logger;
    }

    public async Task<EchoSettings> GetEchoSettingsAsync(ulong guildId)
    {
        var channelId = await GetSettingAsync<ulong?>(guildId, SettingKeys.EchoChannel, null);
        var echoEnabled = await GetSettingAsync(guildId, SettingKeys.EchoEnabled, true);

        return new EchoSettings(channelId, echoEnabled);
    }

    public async Task SaveEchoSettingsAsync(ulong guildId, ulong? channelId, bool? echoEnabled)
    {
        if (channelId.HasValue)
        {
            await SaveSettingAsync(guildId, SettingKeys.EchoChannel, channelId.Value);
        }

        if (echoEnabled.HasValue)
        {
            await SaveSettingAsync(guildId, SettingKeys.EchoEnabled, echoEnabled.Value);
        }
    }

    public async Task<IReadOnlySet<AuditCategory>> GetDisabledAuditCategoriesAsync(ulong guildId)
    {
        var names = await GetSettingAsync<List<string>>(guildId, SettingKeys.DisabledAuditCategories, null) ?? new List<string>();

        return names.Select(name => Enum.TryParse<AuditCategory>(name, out var category) ? category : (AuditCategory?)null)
            .OfType<AuditCategory>()
            .ToHashSet();
    }

    public async Task SaveDisabledAuditCategoriesAsync(ulong guildId, IEnumerable<AuditCategory> categories)
    {
        await SaveSettingAsync(guildId, SettingKeys.DisabledAuditCategories, categories.Distinct().Select(c => c.ToString()).Order().ToList());
    }

    public async Task<GuildTimeZone> GetTimeZoneAsync(ulong guildId)
    {
        return GuildTimeZone.FindOrUtc(await GetSettingAsync<string?>(guildId, SettingKeys.TimeZone, null));
    }

    public async Task SaveTimeZoneAsync(ulong guildId, GuildTimeZone zone)
    {
        await SaveSettingAsync(guildId, SettingKeys.TimeZone, zone.Id);
    }

    private async Task<T?> GetSettingAsync<T>(ulong guildId, string key, T? defaultValue = default)
    {
        var settings = await GetGuildSettingsAsync(guildId);

        if (!settings.TryGetValue(key, out var json))
        {
            return defaultValue;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json) ?? defaultValue;
        }
        catch (JsonException ex)
        {
            // A stored value nobody can read is treated as never set; a database failure is not swallowed here
            _logger.LogWarning(ex, "Setting {Key} for guild {GuildId} is not readable, using the default", key, guildId);
            return defaultValue;
        }
    }

    private async Task SaveSettingAsync<T>(ulong guildId, string key, T value)
    {
        var setting = new GuildSettings
        {
            GuildId = guildId,
            SettingKey = key,
            SettingValue = JsonSerializer.Serialize(value),
            UpdatedAt = DateTime.UtcNow
        };

        await _guilds.SaveSettingAsync(setting);
        _cache.Remove(CacheKey(guildId));
        _logger.LogInformation("Saved setting {Key} for guild {GuildId}", key, guildId);
    }

    private async Task<IReadOnlyDictionary<string, string>> GetGuildSettingsAsync(ulong guildId)
    {
        var cacheKey = CacheKey(guildId);

        if (_cache.TryGetValue(cacheKey, out IReadOnlyDictionary<string, string>? cached) && cached != null)
        {
            return cached;
        }

        var loaded = await _guilds.GetGuildSettingsAsync(guildId);
        _cache.Set(cacheKey, loaded, CacheDuration);
        return loaded;
    }

    private static string CacheKey(ulong guildId)
    {
        return $"guild-settings:{guildId}";
    }
}
