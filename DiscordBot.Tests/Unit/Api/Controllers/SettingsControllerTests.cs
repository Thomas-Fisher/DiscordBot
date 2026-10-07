using DiscordBot.Api.Controllers;
using DiscordBot.Application;
using DiscordBot.Application.Models;
using DiscordBot.Application.Services;
using DiscordBot.Contracts;
using DiscordBot.Domain.Entities;
using DiscordBot.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Unit.Api.Controllers;

public class SettingsControllerTests
{
    private const ulong ManagedGuild = 123;

    private readonly TestDb _db = new();
    private readonly RecordingActivityLogger _activity = new();
    private readonly SettingsService _settings;
    private readonly SettingsController _controller;
    private readonly FakeGuildDirectory _directory = new();

    public SettingsControllerTests()
    {
        _settings = new SettingsService(_db.Guilds, new MemoryCache(new MemoryCacheOptions()), NullLogger<SettingsService>.Instance);
        _directory.Add(TestGuilds.Basic(ManagedGuild), channels: [new GuildChannel(888, "log", 0), new GuildChannel(999, "other", 1)]);
        _controller = new SettingsController(ControllerTestContext.GuildAccess(456, ManagedGuild), _settings, _activity, _directory)
        {
            ControllerContext = ControllerTestContext.Create(456, ManagedGuild)
        };
    }

    [Fact]
    public async Task GetGuildSettings_ChannelSetAndEchoOn_ReturnsThemAndReportsLoggingEnabled()
    {
        await _db.Guilds.SaveSettingAsync(new GuildSettings { GuildId = ManagedGuild, SettingKey = SettingKeys.EchoChannel, SettingValue = "999", UpdatedAt = DateTime.UtcNow });

        var result = await _controller.GetGuildSettings(ManagedGuild);

        var dto = Assert.IsType<GuildSettingsDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal((ManagedGuild, 999UL, true, true), (dto.GuildId, dto.EchoChannelId, dto.EchoEnabled, dto.EchoActive));
    }

    [Fact]
    public async Task UpdateGuildSettings_ChannelAndEchoFlag_AreSavedAndRecordedInTheActivityLog()
    {
        var request = new UpdateSettingsRequest { EchoChannelId = 888, EchoEnabled = false };

        var result = await _controller.UpdateGuildSettings(ManagedGuild, request);

        var dto = Assert.IsType<GuildSettingsDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal((888UL, false), (dto.EchoChannelId, dto.EchoEnabled));
        var saved = await _settings.GetEchoSettingsAsync(ManagedGuild);
        Assert.Equal((888UL, false), (saved.ChannelId, saved.Enabled));
        Assert.Equal(1, _activity.Entries.Count(e => e.Action == "Updated" && e.EntityType == "GuildSettings"));
    }

    [Fact]
    public async Task UpdateGuildSettings_ChannelThatIsNotInTheServer_IsRefusedAndNothingIsSaved()
    {
        var result = await _controller.UpdateGuildSettings(ManagedGuild, new UpdateSettingsRequest { EchoChannelId = 555 });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null((await _settings.GetEchoSettingsAsync(ManagedGuild)).ChannelId);
    }

    [Fact]
    public async Task UpdateGuildSettings_AuditCategories_AreSavedAndReturnedAndAnEmptyListSwitchesThemBackOn()
    {
        var off = await _controller.UpdateGuildSettings(ManagedGuild, new UpdateSettingsRequest { DisabledAuditCategories = new List<string> { "voice", "Reactions" } });
        var disabled = Assert.IsType<GuildSettingsDto>(Assert.IsType<OkObjectResult>(off).Value).DisabledAuditCategories;
        var on = await _controller.UpdateGuildSettings(ManagedGuild, new UpdateSettingsRequest { DisabledAuditCategories = new List<string>() });

        Assert.Equal(new[] { "Reactions", "Voice" }, disabled);
        Assert.Empty(Assert.IsType<GuildSettingsDto>(Assert.IsType<OkObjectResult>(on).Value).DisabledAuditCategories);
    }

    [Fact]
    public async Task UpdateGuildSettings_UnknownAuditCategory_IsRefusedAndNothingIsSaved()
    {
        var result = await _controller.UpdateGuildSettings(ManagedGuild, new UpdateSettingsRequest { DisabledAuditCategories = new List<string> { "Voice", "Gossip" } });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await _settings.GetDisabledAuditCategoriesAsync(ManagedGuild));
    }

    [Fact]
    public async Task UpdateGuildSettings_NoChannelValue_ClearsTheChannel()
    {
        await _controller.UpdateGuildSettings(ManagedGuild, new UpdateSettingsRequest { EchoChannelId = 888 });

        var result = await _controller.UpdateGuildSettings(ManagedGuild, new UpdateSettingsRequest { EchoChannelId = UpdateSettingsRequest.NoChannel });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(UpdateSettingsRequest.NoChannel, (await _settings.GetEchoSettingsAsync(ManagedGuild)).ChannelId);
    }

    [Fact]
    public async Task GuildSettings_GuildTheCallerDoesNotManage_CannotBeReadOrChanged()
    {
        var read = await _controller.GetGuildSettings(999);
        var update = await _controller.UpdateGuildSettings(999, new UpdateSettingsRequest { EchoChannelId = 1 });

        Assert.IsType<ForbidResult>(read);
        Assert.IsType<ForbidResult>(update);
        Assert.Null((await _settings.GetEchoSettingsAsync(999)).ChannelId);
    }

    [Fact]
    public async Task UpdateGuildSettings_TimeZone_IsSavedReturnedAndRecordedInTheActivityLog()
    {
        var result = await _controller.UpdateGuildSettings(ManagedGuild, new UpdateSettingsRequest { TimeZone = "Europe/London" });

        Assert.Equal("Europe/London", Assert.IsType<GuildSettingsDto>(Assert.IsType<OkObjectResult>(result).Value).TimeZone);
        Assert.Equal("Europe/London", (await _settings.GetTimeZoneAsync(ManagedGuild)).Id);
        Assert.Single(_activity.Entries, e => e.Action == "Updated" && e.EntityType == "GuildSettings");
    }

    [Fact]
    public async Task UpdateGuildSettings_UnknownTimeZone_IsABadRequestAndNothingElseInTheRequestIsSaved()
    {
        var result = await _controller.UpdateGuildSettings(ManagedGuild, new UpdateSettingsRequest { TimeZone = "Mars/Olympus_Mons", EchoChannelId = 777 });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null((await _settings.GetEchoSettingsAsync(ManagedGuild)).ChannelId);
        Assert.Empty(_activity.Entries);
    }

    [Fact]
    public async Task UpdateGuildSettings_NoTimeZoneInTheRequest_LeavesTheChosenZoneAlone()
    {
        await _controller.UpdateGuildSettings(ManagedGuild, new UpdateSettingsRequest { TimeZone = "Europe/London" });

        await _controller.UpdateGuildSettings(ManagedGuild, new UpdateSettingsRequest { EchoEnabled = false });

        Assert.Equal("Europe/London", (await _settings.GetTimeZoneAsync(ManagedGuild)).Id);
    }
}
