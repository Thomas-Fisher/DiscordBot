using DiscordBot.Api.Controllers;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Services;
using DiscordBot.Contracts;
using DiscordBot.Domain.Entities;
using DiscordBot.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Unit.Api.Controllers;

public class LogsControllerTests
{
    private const ulong GuildId = 123;

    private readonly TestDb _db = new();
    private readonly FakeGuildDirectory _directory = new();
    private readonly LogsController _controller;

    public LogsControllerTests()
    {
        _controller = CreateController(_db.ActivityLogs);
    }

    private LogsController CreateController(IActivityLogRepository logs) =>
        new(ControllerTestContext.GuildAccess(456, GuildId), new ActivityLogService(logs), _directory, new SettingsService(_db.Guilds, new MemoryCache(new MemoryCacheOptions()), NullLogger<SettingsService>.Instance)) { ControllerContext = ControllerTestContext.Create(456, GuildId) };

    private async Task StoreAsync(ulong id, TimeSpan age, string action = "Created")
    {
        _db.Context.ActivityLogs.Add(new ActivityLog
        {
            LogId = id,
            GuildId = GuildId,
            Timestamp = DateTime.UtcNow - age,
            ActionType = action,
            EntityType = "Event",
            EntityId = 1,
            UserId = 7
        });
        await _db.Context.SaveChangesAsync();
    }

    private async Task StoreManyAsync(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await StoreAsync((ulong)i, TimeSpan.FromMinutes(i));
        }
    }

    private static PaginatedActivityLogsDto Dto(ActionResult<PaginatedActivityLogsDto> result) =>
        Assert.IsType<PaginatedActivityLogsDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task GetActivityLogs_MoreRowsThanAPage_ReturnsACursorThatFetchesTheNextPage()
    {
        await StoreManyAsync(5);
        var first = Dto(await _controller.GetActivityLogs(GuildId, pageSize: 2));

        var second = Dto(await _controller.GetActivityLogs(GuildId, cursor: first.NextCursor, pageSize: 2));

        Assert.Equal(new ulong[] { 1, 2 }, first.Logs.Select(l => l.LogId));
        Assert.NotNull(first.NextCursor);
        Assert.Equal(new ulong[] { 3, 4 }, second.Logs.Select(l => l.LogId));
    }

    [Fact]
    public async Task GetActivityLogs_UserStillInTheServer_ShowsTheirServerNameAndOneWhoLeftHasNone()
    {
        await StoreAsync(1, TimeSpan.FromMinutes(1));
        _db.Context.ActivityLogs.Add(new ActivityLog { LogId = 2, GuildId = GuildId, Timestamp = DateTime.UtcNow, ActionType = "Created", EntityType = "Event", UserId = 8 });
        await _db.Context.SaveChangesAsync();
        _directory.Names[(GuildId, 7)] = "Fireball Fred";

        var dto = Dto(await _controller.GetActivityLogs(GuildId));

        Assert.Equal("Fireball Fred", dto.Logs.Single(l => l.LogId == 1).UserDisplayName);
        Assert.Null(dto.Logs.Single(l => l.LogId == 2).UserDisplayName);
    }

    [Fact]
    public async Task GetActivityLogs_Entry_ComesWithAReadableSentenceUsingTheServerNames()
    {
        _db.Context.ActivityLogs.Add(new ActivityLog
        {
            LogId = 5,
            GuildId = GuildId,
            Timestamp = DateTime.UtcNow,
            ActionType = "VoiceStateChanged",
            EntityType = "VoiceState",
            EntityId = 55,
            UserId = 7,
            Changes = "{\"kind\":\"joined\",\"channelId\":55,\"channelName\":\"General\"}"
        });
        await _db.Context.SaveChangesAsync();
        _directory.Names[(GuildId, 7)] = "Fireball Fred";

        var dto = Dto(await _controller.GetActivityLogs(GuildId));

        Assert.Equal("🔊 Fireball Fred joined #General", dto.Logs.Single(l => l.LogId == 5).Summary);
    }

    [Theory]
    [InlineData("garbage!!")]
    [InlineData("YWJj")]
    public async Task GetActivityLogs_InvalidCursor_IsABadRequest(string cursor)
    {
        var result = await _controller.GetActivityLogs(GuildId, cursor: cursor);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Theory]
    [InlineData(0, new ulong[] { 1 })]            // below the minimum: one day
    [InlineData(-5, new ulong[] { 1 })]
    [InlineData(30, new ulong[] { 1, 2 })]
    [InlineData(365, new ulong[] { 1, 2, 3 })]    // above the maximum: ninety days, so the 100 day old log stays out
    public async Task GetActivityLogs_RequestedDayWindow_IsKeptWithinOneAndNinetyDays(int requestedDays, ulong[] expectedIds)
    {
        await StoreAsync(1, TimeSpan.FromHours(12));
        await StoreAsync(2, TimeSpan.FromDays(20));
        await StoreAsync(3, TimeSpan.FromDays(80));
        await StoreAsync(4, TimeSpan.FromDays(100));

        var dto = Dto(await _controller.GetActivityLogs(GuildId, days: requestedDays));

        Assert.Equal(expectedIds, dto.Logs.Select(l => l.LogId).OrderBy(id => id));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(10_000, 500)]
    public async Task GetActivityLogs_RequestedPageSize_IsKeptWithinOneAndFiveHundred(int requestedSize, int expectedSize)
    {
        await StoreManyAsync(3);

        var dto = Dto(await _controller.GetActivityLogs(GuildId, pageSize: requestedSize));

        Assert.Equal(expectedSize, dto.PageSize);
        Assert.Equal(Math.Min(expectedSize, 3), dto.Logs.Count);
    }

    [Fact]
    public async Task GetActivityLogs_GuildTheCallerDoesNotManage_IsForbidden()
    {
        await StoreManyAsync(1);

        var result = await _controller.GetActivityLogs(999);

        Assert.IsType<ForbidResult>(result.Result);
    }
}
