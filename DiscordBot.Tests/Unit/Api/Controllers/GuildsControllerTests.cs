using DiscordBot.Api.Controllers;
using DiscordBot.Application.Models;
using DiscordBot.Application.Services;
using DiscordBot.Contracts;
using DiscordBot.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using DiscordBot.Domain.Enums;

namespace DiscordBot.Tests.Unit.Api.Controllers;

public class GuildsControllerTests
{
    private const ulong UserId = 123456789;
    private const ulong AllowedGuild = 987654321;

    private readonly TestDb _db = new();

    private GuildsController CreateController(FakeGuildDirectory? directory = null)
    {
        // With no directory given the bot is "in" no guild at all
        var events = new EventService(_db.Events, _db.Guilds, new RecordingActivityLogger(), new RecordingMessageUpdater(), new FakeGuildDirectory(), new FixedTimeProvider());

        return new GuildsController(ControllerTestContext.GuildAccess(UserId, AllowedGuild), directory ?? new FakeGuildDirectory(), events, NullLogger<GuildsController>.Instance)
        {
            ControllerContext = ControllerTestContext.Create(UserId, AllowedGuild)
        };
    }

    private static GuildInfo Info(ulong id, string name = "Raiders") =>
        new(id, name, "icon", "A raiding guild", MemberCount: 42, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc), OwnerId: 7, BoostLevel: 2, RoleCount: 9, ChannelCount: 5);

    public static IEnumerable<object[]> GuildRequests()
    {
        // The caller manages only AllowedGuild, and the bot is in none
        yield return new object[] { "details, guild the caller does not manage", (Func<GuildsController, Task<IActionResult?>>)(async c => (await c.GetGuildDetails(999999999)).Result), typeof(ForbidResult) };
        yield return new object[] { "channels, guild the caller does not manage", (Func<GuildsController, Task<IActionResult?>>)(async c => (await c.GetGuildChannels(999999999)).Result), typeof(ForbidResult) };
        yield return new object[] { "roles, guild the caller does not manage", (Func<GuildsController, Task<IActionResult?>>)(async c => (await c.GetGuildRoles(999999999)).Result), typeof(ForbidResult) };
        yield return new object[] { "roles, managed guild the bot has not joined", (Func<GuildsController, Task<IActionResult?>>)(async c => (await c.GetGuildRoles(AllowedGuild)).Result), typeof(NotFoundObjectResult) };
        yield return new object[] { "members, guild the caller does not manage", (Func<GuildsController, Task<IActionResult?>>)(async c => (await c.SearchGuildMembers(999999999, "a")).Result), typeof(ForbidResult) };
        yield return new object[] { "members, managed guild the bot has not joined", (Func<GuildsController, Task<IActionResult?>>)(async c => (await c.SearchGuildMembers(AllowedGuild, "a")).Result), typeof(NotFoundObjectResult) };
        yield return new object[] { "statistics, guild the caller does not manage", (Func<GuildsController, Task<IActionResult?>>)(async c => (await c.GetGuildStatistics(999999999)).Result), typeof(ForbidResult) };
        yield return new object[] { "details, managed guild the bot has not joined", (Func<GuildsController, Task<IActionResult?>>)(async c => (await c.GetGuildDetails(AllowedGuild)).Result), typeof(NotFoundObjectResult) };
    }

    [Theory]
    [MemberData(nameof(GuildRequests))]
    public async Task GuildEndpoints_InvalidOrUnavailableGuild_AreRefusedWithTheMatchingResult(string scenario, Func<GuildsController, Task<IActionResult?>> call, Type expectedResult)
    {
        var controller = CreateController();

        var result = await call(controller);

        Assert.True(expectedResult.IsInstanceOfType(result), $"{scenario}: expected {expectedResult.Name} but got {result?.GetType().Name}");
    }

    [Fact]
    public async Task GetUserGuilds_ManagesTwoButTheBotIsInOnlyOne_ListsOnlyTheOneItIsIn()
    {
        var controller = CreateController(new FakeGuildDirectory().Add(Info(AllowedGuild, "Raiders")));

        var result = await controller.GetUserGuilds();

        var guilds = Assert.IsType<List<GuildDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var guild = Assert.Single(guilds);
        Assert.Equal((AllowedGuild.ToString(), "Raiders", "icon"), (guild.Id, guild.Name, guild.Icon));
    }

    [Fact]
    public async Task GetGuildDetails_ManagedGuildTheBotIsIn_ReturnsItsDetailsWithIdsAsText()
    {
        var controller = CreateController(new FakeGuildDirectory().Add(Info(AllowedGuild)));

        var result = await controller.GetGuildDetails(AllowedGuild);

        var details = Assert.IsType<GuildDetailsDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(("Raiders", 42, "7"), (details.Name, details.MemberCount, details.OwnerId));
        Assert.Equal(DateTimeKind.Utc, details.CreatedAt.Kind);
        Assert.Null(details.OwnerName);
    }

    [Fact]
    public async Task GetGuildDetails_OwnerIsInTheServer_ReturnsTheNameTheServerShowsForThem()
    {
        var directory = new FakeGuildDirectory().Add(Info(AllowedGuild));
        directory.Names[(AllowedGuild, 7UL)] = "Guild Master";

        var result = await CreateController(directory).GetGuildDetails(AllowedGuild);

        var details = Assert.IsType<GuildDetailsDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Guild Master", details.OwnerName);
    }

    [Fact]
    public async Task SearchGuildMembers_ManagedGuild_ReturnsThePeopleWhoseNameMatchesInNameOrder()
    {
        var directory = new FakeGuildDirectory().Add(Info(AllowedGuild));
        directory.MemberList[AllowedGuild] =
        [
            new GuildMember(3, "Zed the Tank", "zed", null),
            new GuildMember(1, "Alice", "alice_tank", "https://cdn/a.png"),
            new GuildMember(2, "Bob", "bob", null)
        ];

        var result = await CreateController(directory).SearchGuildMembers(AllowedGuild, " TANK ");

        var members = Assert.IsType<List<MemberDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new ulong[] { 1, 3 }, members.Select(m => m.Id));
        Assert.Equal(("Alice", "alice_tank", "https://cdn/a.png"), (members[0].DisplayName, members[0].Username, members[0].AvatarUrl));
    }

    [Fact]
    public async Task SearchGuildMembers_MoreMatchesThanTheLimit_ReturnsOnlyTheFirstFew()
    {
        var directory = new FakeGuildDirectory().Add(Info(AllowedGuild));
        directory.MemberList[AllowedGuild] = Enumerable.Range(1, GuildsController.MaxMemberResults + 10).Select(i => new GuildMember((ulong)i, $"Player{i:000}", $"p{i}", null)).ToList();

        var result = await CreateController(directory).SearchGuildMembers(AllowedGuild);

        Assert.Equal(GuildsController.MaxMemberResults, Assert.IsType<List<MemberDto>>(Assert.IsType<OkObjectResult>(result.Result).Value).Count);
    }

    [Fact]
    public async Task GetGuildChannels_ManagedGuild_ListsTheTextChannelsAsTextChannels()
    {
        var directory = new FakeGuildDirectory().Add(Info(AllowedGuild), channels: [new GuildChannel(11, "general", 0), new GuildChannel(12, "raids", 1)]);

        var result = await CreateController(directory).GetGuildChannels(AllowedGuild);

        var channels = Assert.IsType<List<ChannelDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new[] { "general", "raids" }, channels.Select(c => c.Name));
        Assert.All(channels, c => Assert.Equal(ChannelDto.TextChannelType, c.Type));
    }

    [Fact]
    public async Task GetGuildRoles_ManagedGuild_ListsTheRolesTheDirectoryOffers()
    {
        var directory = new FakeGuildDirectory().Add(Info(AllowedGuild), roles: [new GuildRole(21, "Raider", 3)]);

        var result = await CreateController(directory).GetGuildRoles(AllowedGuild);

        var role = Assert.Single(Assert.IsType<List<RoleDto>>(Assert.IsType<OkObjectResult>(result.Result).Value));
        Assert.Equal(("21", "Raider"), (role.Id, role.Name));
    }

    [Fact]
    public async Task GetGuildStatistics_ManagedGuild_CombinesTheServersFiguresWithTheEventCounts()
    {
        var controller = CreateController(new FakeGuildDirectory().Add(Info(AllowedGuild)));
        await _db.Events.CreateEventAsync(TestEvents.Scheduled(1, guildId: AllowedGuild));
        var finished = TestEvents.Scheduled(2, guildId: AllowedGuild).WithStatus(EventStatus.Completed);
        finished.ScheduledTime = DateTime.UtcNow.AddDays(-3);
        await _db.Events.CreateEventAsync(finished);
        await _db.Events.CreateEventAsync(TestEvents.Scheduled(3, guildId: AllowedGuild).WithStatus(EventStatus.Cancelled));

        var result = await controller.GetGuildStatistics(AllowedGuild);

        var statistics = Assert.IsType<GuildStatisticsDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal((42, 9, 5, 2), (statistics.MemberCount, statistics.RolesCount, statistics.ChannelsCount, statistics.BoostLevel));
        Assert.Equal(2, statistics.TotalEvents);   // the cancelled one is not counted
        Assert.Equal(1, statistics.UpcomingEvents);
    }
}
