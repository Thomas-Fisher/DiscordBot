using DiscordBot.Application;
using DiscordBot.Application.Services;
using DiscordBot.Domain.Common;
using DiscordBot.Domain.Entities;
using DiscordBot.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Unit.Application;

public class SettingsServiceTests
{
    private const ulong GuildId = 123;

    private readonly TestDb _db = new();
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _service = new SettingsService(_db.Guilds, new MemoryCache(new MemoryCacheOptions()), NullLogger<SettingsService>.Instance);
    }

    /// <summary>Writes a setting straight into the database, behind the service's back.</summary>
    private async Task StoreSettingAsync(ulong guildId, string key, string jsonValue)
    {
        using var context = _db.Open();
        var existing = await context.GuildSettings.SingleOrDefaultAsync(s => s.GuildId == guildId && s.SettingKey == key);
        if (existing != null)
        {
            existing.SettingValue = jsonValue;
        }
        else
        {
            context.GuildSettings.Add(new GuildSettings { GuildId = guildId, SettingKey = key, SettingValue = jsonValue, UpdatedAt = DateTime.UtcNow });
        }

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetEchoSettings_StoredValues_AreReturned()
    {
        await StoreSettingAsync(GuildId, SettingKeys.EchoChannel, "456");
        await StoreSettingAsync(GuildId, SettingKeys.EchoEnabled, "false");

        var settings = await _service.GetEchoSettingsAsync(GuildId);

        Assert.Equal((456UL, false), (settings.ChannelId, settings.Enabled));
    }

    [Fact]
    public async Task GetEchoSettings_NothingStored_DefaultsToEchoOnWithNoChannel()
    {
        var settings = await _service.GetEchoSettingsAsync(GuildId);

        Assert.Equal((null, true), (settings.ChannelId, settings.Enabled));
    }

    [Fact]
    public async Task GetEchoSettings_UnreadableStoredValue_FallsBackToTheDefault()
    {
        await StoreSettingAsync(GuildId, SettingKeys.EchoEnabled, "not json");

        var settings = await _service.GetEchoSettingsAsync(GuildId);

        Assert.True(settings.Enabled);
    }

    [Fact]
    public async Task GetEchoSettings_ReadAgainAfterTheDatabaseChanged_StillReturnsTheCachedValue()
    {
        await StoreSettingAsync(GuildId, SettingKeys.EchoChannel, "1");
        await _service.GetEchoSettingsAsync(GuildId);
        await StoreSettingAsync(GuildId, SettingKeys.EchoChannel, "2");

        var settings = await _service.GetEchoSettingsAsync(GuildId);

        Assert.Equal(1UL, settings.ChannelId);
    }

    [Fact]
    public async Task SaveEchoSettings_AfterAValueWasCached_ReadsBackTheNewValue()
    {
        await StoreSettingAsync(GuildId, SettingKeys.EchoChannel, "1");
        await _service.GetEchoSettingsAsync(GuildId);

        await _service.SaveEchoSettingsAsync(GuildId, 2, null);
        var settings = await _service.GetEchoSettingsAsync(GuildId);

        Assert.Equal(2UL, settings.ChannelId);
    }

    [Fact]
    public async Task SaveEchoSettings_OnlyTheChannel_LeavesTheEchoSettingAsItWas()
    {
        await StoreSettingAsync(GuildId, SettingKeys.EchoEnabled, "false");

        await _service.SaveEchoSettingsAsync(GuildId, 456, null);

        var settings = await _service.GetEchoSettingsAsync(GuildId);
        Assert.Equal((456UL, false), (settings.ChannelId, settings.Enabled));
    }

    [Fact]
    public async Task GetTimeZone_NothingStored_IsUtc()
    {
        var zone = await _service.GetTimeZoneAsync(GuildId);

        Assert.Equal("UTC", zone.Id);
    }

    [Fact]
    public async Task SaveTimeZone_Zone_IsReturnedForThatGuildOnly()
    {
        GuildTimeZone.TryFind("Europe/London", out var london);

        await _service.SaveTimeZoneAsync(GuildId, london);

        Assert.Equal("Europe/London", (await _service.GetTimeZoneAsync(GuildId)).Id);
        Assert.Equal("UTC", (await _service.GetTimeZoneAsync(GuildId + 1)).Id);
    }

    [Fact]
    public async Task GetTimeZone_StoredNameThatThisMachineDoesNotKnow_FallsBackToUtcInsteadOfFailing()
    {
        await StoreSettingAsync(GuildId, SettingKeys.TimeZone, "\"Mars/Olympus_Mons\"");

        var zone = await _service.GetTimeZoneAsync(GuildId);

        Assert.Equal("UTC", zone.Id);
    }

    [Fact]
    public async Task SaveTimeZone_ChangedAfterItWasRead_TheNewValueIsUsedStraightAway()
    {
        GuildTimeZone.TryFind("Europe/London", out var london);
        GuildTimeZone.TryFind("America/New_York", out var newYork);
        await _service.SaveTimeZoneAsync(GuildId, london);
        await _service.GetTimeZoneAsync(GuildId);

        await _service.SaveTimeZoneAsync(GuildId, newYork);

        Assert.Equal("America/New_York", (await _service.GetTimeZoneAsync(GuildId)).Id);
    }
}
