using DiscordBot.Domain.Entities;
using DiscordBot.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Tests.Unit.Persistence;

public class GuildRepositoryTests
{
    private readonly TestDb _db = new();

    [Fact]
    public async Task SaveGuild_KnownGuild_UpdatesTheNameButKeepsTheOriginalCreationTime()
    {
        var firstSeen = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await _db.Guilds.SaveGuildAsync(new Guild { GuildId = 1, GuildName = "Old name", CreatedAt = firstSeen, UpdatedAt = firstSeen });

        // What the guild sync does at every start-up: it builds a fresh Guild with CreatedAt = now
        await _db.Guilds.SaveGuildAsync(new Guild { GuildId = 1, GuildName = "New name", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        var stored = await _db.Context.Guilds.SingleAsync();
        Assert.Equal(("New name", firstSeen), (stored.GuildName, stored.CreatedAt));
        Assert.True(stored.UpdatedAt > firstSeen);
    }

    [Fact]
    public async Task SaveSetting_GuildWithNoRowYet_CreatesTheGuildSoTheSettingHasSomethingToBelongTo()
    {
        await _db.Guilds.SaveSettingAsync(new GuildSettings { GuildId = 55, SettingKey = "time_zone", SettingValue = "\"UTC\"", UpdatedAt = DateTime.UtcNow });

        Assert.Equal(55UL, (await _db.Context.Guilds.SingleAsync()).GuildId);
        Assert.Single(await _db.Context.GuildSettings.ToListAsync());
    }

    [Fact]
    public async Task EnsureExists_UnknownGuild_CreatesIt()
    {
        await _db.Guilds.EnsureExistsAsync(7);

        Assert.Equal(7UL, (await _db.Context.Guilds.SingleAsync()).GuildId);
    }

    [Fact]
    public async Task EnsureExists_KnownGuild_LeavesItUntouched()
    {
        var firstSeen = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await _db.Guilds.SaveGuildAsync(new Guild { GuildId = 7, GuildName = "Kept", CreatedAt = firstSeen, UpdatedAt = firstSeen });

        await _db.Guilds.EnsureExistsAsync(7);

        var stored = await _db.Context.Guilds.SingleAsync();
        Assert.Equal(("Kept", firstSeen), (stored.GuildName, stored.CreatedAt));
    }

    [Fact]
    public async Task GetGuildSettings_SeveralGuilds_ReturnsEveryKeyOfThatGuildOnly()
    {
        await _db.Guilds.SaveSettingAsync(new GuildSettings { GuildId = 1, SettingKey = "log_channel", SettingValue = "5", UpdatedAt = DateTime.UtcNow });
        await _db.Guilds.SaveSettingAsync(new GuildSettings { GuildId = 1, SettingKey = "log_messages", SettingValue = "false", UpdatedAt = DateTime.UtcNow });
        await _db.Guilds.SaveSettingAsync(new GuildSettings { GuildId = 2, SettingKey = "log_channel", SettingValue = "7", UpdatedAt = DateTime.UtcNow });

        var settings = await _db.Guilds.GetGuildSettingsAsync(1);

        Assert.Equal(new Dictionary<string, string> { ["log_channel"] = "5", ["log_messages"] = "false" }, settings);
    }
}
