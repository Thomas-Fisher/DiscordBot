using DiscordBot.Api.Controllers;
using DiscordBot.Application.Services;
using DiscordBot.Contracts;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using DiscordBot.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Tests.Unit.Api.Controllers;

public class SignupsControllerTests
{
    private const ulong ManagedGuild = TestEvents.GuildId;
    private const ulong OtherGuild = 999;

    private readonly TestDb _db = new();
    private readonly FakeGuildDirectory _directory = new();
    private readonly SignupsController _controller;

    public SignupsControllerTests()
    {
        var signups = new SignupService(_db.Signups, new RecordingActivityLogger(), new RecordingMessageUpdater(), new FixedTimeProvider());
        _directory.Members.Add((ManagedGuild, 42));
        _directory.Members.Add((ManagedGuild, 1));
        var events = new EventService(_db.Events, _db.Guilds, new RecordingActivityLogger(), new RecordingMessageUpdater(), new FakeGuildDirectory(), new FixedTimeProvider());
        _controller = new SignupsController(ControllerTestContext.GuildAccess(456, ManagedGuild), events, signups, _directory)
        {
            ControllerContext = ControllerTestContext.Create(456, ManagedGuild)
        };
    }

    private async Task<Event> StoreAsync(Event evt)
    {
        await _db.Events.CreateEventAsync(evt);
        return evt;
    }

    private async Task<EventSignup?> StoredSignupAsync(ulong eventId, ulong userId)
    {
        using var context = _db.Open();
        return await context.EventSignups.AsNoTracking().SingleOrDefaultAsync(s => s.EventId == eventId && s.UserId == userId);
    }

    // Every endpoint, so the access checks can be run against each of them
    public static IEnumerable<object[]> Endpoints()
    {
        yield return new object[] { "GetEventSignups", (Func<SignupsController, ulong, Task<IActionResult?>>)(async (c, id) => (await c.GetEventSignups(id)).Result) };
        yield return new object[] { "CreateSignup", (Func<SignupsController, ulong, Task<IActionResult?>>)(async (c, id) => await c.CreateSignup(id, new CreateSignupRequest { UserId = 1 })) };
        yield return new object[] { "UpdateSignup", (Func<SignupsController, ulong, Task<IActionResult?>>)(async (c, id) => await c.UpdateSignup(id, 1, new UpdateSignupRequest { Status = "SignedOff" })) };
        yield return new object[] { "DeleteSignup", (Func<SignupsController, ulong, Task<IActionResult?>>)(async (c, id) => await c.DeleteSignup(id, 1)) };
        yield return new object[] { "SetRoster", (Func<SignupsController, ulong, Task<IActionResult?>>)(async (c, id) => await c.SetRoster(id, new SetRosterRequest())) };
    }

    [Fact]
    public async Task GetEventSignups_EventWithSignups_ReturnsThemInSignupOrder()
    {
        var evt = await StoreAsync(TestEvents.Wow());
        await _db.Signups.SignUpAsync(evt.EventId, 20, "mage", "fire");
        await _db.Signups.SignUpAsync(evt.EventId, 10, "priest", "holy");

        var result = await _controller.GetEventSignups(evt.EventId);

        var list = Assert.IsType<List<SignupDetailsDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new ulong[] { 20, 10 }, list.Select(s => s.UserId));
        Assert.Equal("SignedUp", list[0].Status);
    }

    [Fact]
    public async Task SetRoster_PeopleSignedUp_StoresTheirGroupsAndGetEventSignupsReportsThem()
    {
        var evt = await StoreAsync(TestEvents.Wow());
        await _db.Signups.SignUpAsync(evt.EventId, 20, "mage", "fire");
        await _db.Signups.SignUpAsync(evt.EventId, 10, "priest", "holy");

        var result = await _controller.SetRoster(evt.EventId, new SetRosterRequest { Assignments = [new RosterAssignment { UserId = 20, Group = 2 }] });

        Assert.IsType<NoContentResult>(result);
        var list = Assert.IsType<List<SignupDetailsDto>>(Assert.IsType<OkObjectResult>((await _controller.GetEventSignups(evt.EventId)).Result).Value);
        Assert.Equal(2, list.Single(s => s.UserId == 20).Group);
        Assert.Null(list.Single(s => s.UserId == 10).Group);
    }

    [Fact]
    public async Task SetRoster_SamePersonListedTwice_IsABadRequest()
    {
        var evt = await StoreAsync(TestEvents.Wow());

        var result = await _controller.SetRoster(evt.EventId, new SetRosterRequest { Assignments = [new RosterAssignment { UserId = 1, Group = 1 }, new RosterAssignment { UserId = 1, Group = 2 }] });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SetRoster_RuleBroken_IsABadRequestWithTheRulesCode()
    {
        var evt = await StoreAsync(TestEvents.Scheduled());

        var result = await _controller.SetRoster(evt.EventId, new SetRosterRequest());

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);
        Assert.Equal("roster.not_a_wow_event", problem.Extensions["code"]);
    }

    [Fact]
    public async Task GetEventSignups_MemberWithAServerName_ShowsItAndAMemberWhoLeftHasNone()
    {
        var evt = await StoreAsync(TestEvents.Wow());
        await _db.Signups.SignUpAsync(evt.EventId, 20, "mage", "fire");
        await _db.Signups.SignUpAsync(evt.EventId, 10, "priest", "holy");
        _directory.Names[(ManagedGuild, 20)] = "Fireball Fred";

        var result = await _controller.GetEventSignups(evt.EventId);

        var list = Assert.IsType<List<SignupDetailsDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Fireball Fred", list.Single(s => s.UserId == 20).DisplayName);
        Assert.Null(list.Single(s => s.UserId == 10).DisplayName);
    }

    [Fact]
    public async Task CreateSignup_ValidRequest_RecordsTheSignupAndReturnsThePlaceInTheLine()
    {
        var evt = await StoreAsync(TestEvents.Wow());

        var result = await _controller.CreateSignup(evt.EventId, new CreateSignupRequest { UserId = 42, WowClass = "mage", WowSpec = "fire" });

        Assert.Equal(1, Assert.IsType<SignupCreatedDto>(Assert.IsType<OkObjectResult>(result).Value).Order);
        var stored = await StoredSignupAsync(evt.EventId, 42);
        Assert.Equal((SignupStatus.SignedUp, "mage", "fire"), (stored!.Status, stored.WowClass, stored.WowSpec));
    }

    [Fact]
    public async Task CreateSignup_NoUserId_IsABadRequestAndNothingIsStored()
    {
        var evt = await StoreAsync(TestEvents.Wow());

        var result = await _controller.CreateSignup(evt.EventId, new CreateSignupRequest { UserId = 0 });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(await StoredSignupAsync(evt.EventId, 0));
    }

    [Fact]
    public async Task CreateSignup_UserAlreadySignedUpAndNoNewSpecGiven_IsABadRequestAndNothingChanges()
    {
        var evt = await StoreAsync(TestEvents.Wow());
        await _db.Signups.SignUpAsync(evt.EventId, 42, "mage", "fire");

        var result = await _controller.CreateSignup(evt.EventId, new CreateSignupRequest { UserId = 42 });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("fire", (await StoredSignupAsync(evt.EventId, 42))!.WowSpec);
    }

    [Fact]
    public async Task UpdateSignup_UserWithNoSignup_IsNotFound()
    {
        var evt = await StoreAsync(TestEvents.Wow());

        var result = await _controller.UpdateSignup(evt.EventId, 42, new UpdateSignupRequest { Status = "SignedOff" });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DeleteSignup_UserWithNoSignup_IsNotFound()
    {
        var evt = await StoreAsync(TestEvents.Wow());

        var result = await _controller.DeleteSignup(evt.EventId, 42);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_EventInAGuildTheCallerDoesNotManage_IsNotFoundAndNothingIsStored(string endpoint, Func<SignupsController, ulong, Task<IActionResult?>> call)
    {
        var foreign = await StoreAsync(TestEvents.Wow(900).WithGuild(OtherGuild));

        var result = await call(_controller, foreign.EventId);

        Assert.True(result is NotFoundObjectResult, $"{endpoint} returned {result?.GetType().Name}");
        Assert.Null(await StoredSignupAsync(foreign.EventId, 1));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_DeletedOrMissingEvent_IsNotFound(string endpoint, Func<SignupsController, ulong, Task<IActionResult?>> call)
    {
        var deleted = await StoreAsync(TestEvents.Wow(901).Deleted());

        var forDeleted = await call(_controller, deleted.EventId);
        var forMissing = await call(_controller, 12345);

        Assert.True(forDeleted is NotFoundObjectResult, $"{endpoint} for a deleted event returned {forDeleted?.GetType().Name}");
        Assert.True(forMissing is NotFoundObjectResult, $"{endpoint} for a missing event returned {forMissing?.GetType().Name}");
    }

    [Fact]
    public async Task CreateSignup_PersonWhoIsNotInTheServer_IsRefusedAndNothingIsStored()
    {
        var evt = await StoreAsync(TestEvents.Scheduled());

        var result = await _controller.CreateSignup(evt.EventId, new CreateSignupRequest { UserId = 777 });

        var refused = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("signup.not_a_member", ((Microsoft.AspNetCore.Mvc.ProblemDetails)refused.Value!).Extensions["code"]);
        Assert.Null(await StoredSignupAsync(evt.EventId, 777));
    }
}
