using DiscordBot.Api.Controllers;
using DiscordBot.Application.Errors;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Application.Services;
using DiscordBot.Contracts;
using DiscordBot.Domain.Entities;
using DiscordBot.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DiscordBot.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Unit.Api.Controllers;

public class EventsControllerTests
{
    private const ulong ManagedGuild = TestEvents.GuildId;
    private const ulong OtherGuild = 999;
    private const ulong CallerId = 456;

    private const ulong AnnouncementChannel = 777;

    private readonly TestDb _db = new();
    private readonly FakeGuildDirectory _directory = new();
    private readonly FakeEventAnnouncer _announcer = new();
    private readonly FakePingSender _pingSender = new();
    private readonly FixedTimeProvider _clock = new();
    private readonly EventsController _controller;

    public EventsControllerTests()
    {
        _directory.Add(TestGuilds.Basic(ManagedGuild), channels: [new GuildChannel(AnnouncementChannel, "raids", 0)]);
        _controller = NewController(ControllerTestContext.GuildAccess(CallerId, ManagedGuild));
    }

    private EventsController NewController(IGuildAccessService guildAccess)
    {
        var service = new EventService(_db.Events, _db.Guilds, new RecordingActivityLogger(), new RecordingMessageUpdater(), new FakeGuildDirectory(), _clock);
        var creator = new EventCreator(service, _announcer, NullLogger<EventCreator>.Instance);

        var pings = new EventPingService(_db.Signups, _directory, _pingSender, new RecordingActivityLogger(), new MemoryCache(new MemoryCacheOptions()), _clock, NullLogger<EventPingService>.Instance);

        return new EventsController(guildAccess, service, NewSignupService(), creator, pings, _directory)
        {
            ControllerContext = ControllerTestContext.Create(CallerId, ManagedGuild)
        };
    }

    private CreateEventRequest NewEventRequest() => new()
    {
        Title = "Raid night",
        Description = "Bring flasks",
        ScheduledTime = _clock.UtcNow.AddDays(3),
        Type = "WowClassic",
        ChannelId = AnnouncementChannel,
        DurationMinutes = 120,
        MentionRoleIds = [11, 12]
    };

    private SignupService NewSignupService() => new(_db.Signups, new RecordingActivityLogger(), new RecordingMessageUpdater(), new FixedTimeProvider());

    private async Task<Event> StoreAsync(Event evt)
    {
        await _db.Events.CreateEventAsync(evt);
        return evt;
    }

    private async Task<Event> StoredAsync(ulong eventId)
    {
        using var context = _db.Open();
        return await context.Events.AsNoTracking().SingleAsync(e => e.EventId == eventId);
    }

    // The endpoints that change an event, so the access checks can be run against each of them
    public static IEnumerable<object[]> ChangingEndpoints()
    {
        yield return new object[] { "UpdateEvent", (Func<EventsController, ulong, Task<ActionResult>>)((c, id) => c.UpdateEvent(id, new UpdateEventRequest { Title = "New title" })) };
        yield return new object[] { "CancelEvent", (Func<EventsController, ulong, Task<ActionResult>>)((c, id) => c.CancelEvent(id)) };
        yield return new object[] { "DeleteEvent", (Func<EventsController, ulong, Task<ActionResult>>)((c, id) => c.DeleteEvent(id)) };
        yield return new object[] { "CancelRecurringEventSeries", (Func<EventsController, ulong, Task<ActionResult>>)((c, id) => c.CancelRecurringEventSeries(id)) };
    }

    [Fact]
    public async Task GetEventById_EventInAGuildTheCallerManages_ReturnsItsDetailsAndSignups()
    {
        var evt = await StoreAsync(TestEvents.Wow());
        await _db.Signups.SignUpAsync(evt.EventId, 42, "mage", "fire");

        var result = await _controller.GetEventById(evt.EventId);

        var detail = Assert.IsType<EventDetailDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal((evt.EventId, ManagedGuild, "Raid night"), (detail.EventId, detail.GuildId, detail.Title));
        Assert.Equal(42UL, Assert.Single(detail.Signups).UserId);
    }

    [Fact]
    public async Task GetGuildEvents_EventsWithSignups_ReportHowManyAreSignedUpAndUnsure()
    {
        var busy = await StoreAsync(TestEvents.Wow(1));
        var quiet = await StoreAsync(TestEvents.Scheduled(2));
        await _db.Signups.SignUpAsync(busy.EventId, 42, "mage", "fire");
        await _db.Signups.SignUpAsync(busy.EventId, 43, "priest", "holy");
        await _db.Signups.MarkUnsureAsync(busy.EventId, 44, compactOrder: false);

        var result = await _controller.GetGuildEvents(ManagedGuild);

        var events = Assert.IsAssignableFrom<IEnumerable<EventDto>>(Assert.IsType<OkObjectResult>(result.Result).Value).ToList();
        Assert.Equal((2, 1), (events.Single(e => e.EventId == busy.EventId).SignedUpCount, events.Single(e => e.EventId == busy.EventId).UnsureCount));
        Assert.Equal((0, 0), (events.Single(e => e.EventId == quiet.EventId).SignedUpCount, events.Single(e => e.EventId == quiet.EventId).UnsureCount));
    }

    [Fact]
    public async Task CreateEvent_ValidRequest_StoresTheEventAnnouncesItInTheChosenChannelAndReturnsItsId()
    {
        var result = await _controller.CreateEvent(ManagedGuild, NewEventRequest());

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var eventId = Assert.IsType<EventCreatedDto>(created.Value).EventId;
        var stored = await StoredAsync(eventId);
        Assert.Equal((ManagedGuild, CallerId, "Raid night", EventType.WowClassic), (stored.GuildId, stored.CreatedBy, stored.Title, stored.Type));
        Assert.Equal((AnnouncementChannel, eventId + 1), (stored.ChannelId, stored.MessageId));
        Assert.Equal(new ulong[] { 11, 12 }, stored.MentionRoleIds);
        Assert.Equal(120, stored.DurationMinutes);
        Assert.False(stored.IsRecurring);
        Assert.Equal((eventId, AnnouncementChannel), Assert.Single(_announcer.Posted));
    }

    [Fact]
    public async Task CreateEvent_WithAReminder_StoresItAndLeavingItOutStoresNone()
    {
        var with = await _controller.CreateEvent(ManagedGuild, NewEventRequest() with { ReminderMinutes = 1440 });
        var without = await _controller.CreateEvent(ManagedGuild, NewEventRequest());

        Assert.Equal(1440, (await StoredAsync(Assert.IsType<EventCreatedDto>(Assert.IsType<CreatedAtActionResult>(with.Result).Value).EventId)).ReminderMinutesBefore);
        Assert.Null((await StoredAsync(Assert.IsType<EventCreatedDto>(Assert.IsType<CreatedAtActionResult>(without.Result).Value).EventId)).ReminderMinutesBefore);
    }

    [Fact]
    public async Task UpdateEvent_ReminderChangedAndThenRemoved_IsStoredThenCleared()
    {
        var evt = await StoreAsync(TestEvents.Scheduled());

        await _controller.UpdateEvent(evt.EventId, new UpdateEventRequest { ReminderMinutes = 720 });
        var set = (await StoredAsync(evt.EventId)).ReminderMinutesBefore;
        await _controller.UpdateEvent(evt.EventId, new UpdateEventRequest { ReminderMinutes = 0 });

        Assert.Equal(720, set);
        Assert.Null((await StoredAsync(evt.EventId)).ReminderMinutesBefore);
    }

    [Fact]
    public async Task GetEventById_EventWithAReminder_ReportsIt()
    {
        var evt = await StoreAsync(TestEvents.Scheduled());

        var result = await _controller.GetEventById(evt.EventId);

        Assert.Equal(30, Assert.IsType<EventDetailDto>(Assert.IsType<OkObjectResult>(result.Result).Value).ReminderMinutes);
    }

    [Fact]
    public async Task CreateEvent_WithARepeatPattern_StoresARecurringEvent()
    {
        var result = await _controller.CreateEvent(ManagedGuild, NewEventRequest() with { Recurrence = "EveryFriday" });

        var stored = await StoredAsync(Assert.IsType<EventCreatedDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value).EventId);
        Assert.True(stored.IsRecurring);
        Assert.Equal(RecurrencePattern.EveryFriday, stored.RecurrencePattern);
    }

    [Fact]
    public async Task CreateEvent_ChannelThatIsNotATextChannelOfTheServer_IsRejectedAndNothingIsStoredOrPosted()
    {
        var result = await _controller.CreateEvent(ManagedGuild, NewEventRequest() with { ChannelId = 1 });

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal(EventErrors.UnknownChannel.Code, problem.Extensions["code"]);
        Assert.Empty(_announcer.Posted);
        Assert.Empty(_db.Context.Events);
    }

    [Theory]
    [InlineData("Bingo", null, "event.unknown_type")]
    [InlineData("Generic", "Hourly", "event.unknown_recurrence")]
    public async Task CreateEvent_UnknownTypeOrRepeatPattern_IsRejected(string type, string? recurrence, string expectedCode)
    {
        var result = await _controller.CreateEvent(ManagedGuild, NewEventRequest() with { Type = type, Recurrence = recurrence });

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal(expectedCode, problem.Extensions["code"]);
        Assert.Empty(_announcer.Posted);
    }

    [Fact]
    public async Task CreateEvent_TimeInThePast_IsRejectedWithTheRulesWordingAndNothingIsPosted()
    {
        var result = await _controller.CreateEvent(ManagedGuild, NewEventRequest() with { ScheduledTime = _clock.UtcNow.AddHours(-1) });

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal(EventErrors.DateMustBeInFuture.Code, problem.Extensions["code"]);
        Assert.Empty(_announcer.Posted);
    }

    [Fact]
    public async Task CreateEvent_AnnouncementCannotBePosted_RemovesTheEventAndSaysWhy()
    {
        _announcer.Fail = true;

        var result = await _controller.CreateEvent(ManagedGuild, NewEventRequest());

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal(EventErrors.AnnouncementFailed.Code, problem.Extensions["code"]);
        var stored = await _db.Context.Events.AsNoTracking().SingleAsync();
        Assert.True(stored.IsDeleted);
    }

    [Fact]
    public async Task CreateEvent_GuildTheCallerDoesNotManage_IsForbiddenAndNothingIsCreated()
    {
        _directory.Add(TestGuilds.Basic(OtherGuild), channels: [new GuildChannel(AnnouncementChannel, "raids", 0)]);

        var result = await _controller.CreateEvent(OtherGuild, NewEventRequest());

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Empty(_announcer.Posted);
        Assert.Empty(_db.Context.Events);
    }

    [Fact]
    public async Task GetEventById_SignupWhoLeftTheServer_HasNoDisplayNameWhileOthersShowTheirServerName()
    {
        var evt = await StoreAsync(TestEvents.Wow());
        await _db.Signups.SignUpAsync(evt.EventId, 42, "mage", "fire");
        await _db.Signups.SignUpAsync(evt.EventId, 43, "priest", "holy");
        _directory.Names[(ManagedGuild, 42)] = "Fireball Fred";

        var result = await _controller.GetEventById(evt.EventId);

        var signups = Assert.IsType<EventDetailDto>(Assert.IsType<OkObjectResult>(result.Result).Value).Signups;
        Assert.Equal("Fireball Fred", signups.Single(s => s.UserId == 42).DisplayName);
        Assert.Null(signups.Single(s => s.UserId == 43).DisplayName);
    }

    [Fact]
    public async Task PreviewPing_Roster_ListsWhoWouldBePingedWithTheirServerNamesAndGroups()
    {
        var evt = await StoreAsync(TestEvents.Wow());
        await _db.Signups.SignUpAsync(evt.EventId, 42, "mage", "fire");
        await _db.Signups.SetRosterAsync(evt.EventId, new Dictionary<ulong, int> { [42] = 2 });
        _directory.Names[(ManagedGuild, 42)] = "Fireball Fred";

        var result = await _controller.PreviewPing(evt.EventId, new PingRequestDto { Target = "Roster" });

        var recipient = Assert.Single(Assert.IsType<PingPreviewDto>(Assert.IsType<OkObjectResult>(result.Result).Value).Recipients);
        Assert.Equal((42UL, "Fireball Fred", 2), (recipient.UserId, recipient.DisplayName, recipient.Group));
        Assert.Empty(_pingSender.Sent);
    }

    [Fact]
    public async Task SendPing_Standby_PostsThePingAndReportsHowManyWerePinged()
    {
        var evt = await StoreAsync(TestEvents.Wow());
        await _db.Signups.SignUpAsync(evt.EventId, 42, "mage", "fire");

        var result = await _controller.SendPing(evt.EventId, new PingRequestDto { Target = "standby", Note = "Be ready" });

        Assert.Equal(1, Assert.IsType<PingResultDto>(Assert.IsType<OkObjectResult>(result.Result).Value).Count);
        Assert.Equal((PingTarget.Standby, "Be ready"), (Assert.Single(_pingSender.Sent).Target, _pingSender.Sent[0].Note));
    }

    [Fact]
    public async Task SendPing_UnknownTarget_IsABadRequest()
    {
        var evt = await StoreAsync(TestEvents.Wow());

        var result = await _controller.SendPing(evt.EventId, new PingRequestDto { Target = "Everyone" });

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal(PingErrors.UnknownTarget.Code, problem.Extensions["code"]);
    }

    [Fact]
    public async Task SendPing_NobodyToPing_IsABadRequestWithTheRulesCode()
    {
        var evt = await StoreAsync(TestEvents.Wow());

        var result = await _controller.SendPing(evt.EventId, new PingRequestDto { Target = "Roster" });

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal(PingErrors.NoOneToPing.Code, problem.Extensions["code"]);
    }

    [Fact]
    public async Task Ping_EventInAGuildTheCallerDoesNotManage_IsNotFoundAndNothingIsPosted()
    {
        var foreign = await StoreAsync(TestEvents.Scheduled(900, EventType.WowClassic, OtherGuild));

        var preview = await _controller.PreviewPing(foreign.EventId, new PingRequestDto { Target = "Roster" });
        var send = await _controller.SendPing(foreign.EventId, new PingRequestDto { Target = "Roster" });

        Assert.IsType<NotFoundObjectResult>(preview.Result);
        Assert.IsType<NotFoundObjectResult>(send.Result);
        Assert.Empty(_pingSender.Sent);
    }

    [Fact]
    public async Task GetEventById_EventInAnotherGuild_IsNotFoundSoItsExistenceIsNotRevealed()
    {
        var evt = await StoreAsync(TestEvents.Scheduled(guildId: OtherGuild));

        var result = await _controller.GetEventById(evt.EventId);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetEventById_NoSuchEvent_IsNotFound()
    {
        var result = await _controller.GetEventById(12345);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateEvent_FormThatPostsEveryFieldButChangesOnlyTheTitle_ChangesOnlyTheTitle()
    {
        // The web form always posts every field; the time comes back without seconds
        var evt = TestEvents.Scheduled();
        evt.ScheduledTime = new DateTime(2099, 10, 1, 19, 30, 41, DateTimeKind.Utc);
        await StoreAsync(evt);

        var result = await _controller.UpdateEvent(evt.EventId, new UpdateEventRequest
        {
            Title = "Only the title changed",
            Description = evt.Description,
            ScheduledTime = new DateTime(2099, 10, 1, 19, 30, 0, DateTimeKind.Utc)
        });

        Assert.IsType<NoContentResult>(result);
        var stored = await StoredAsync(evt.EventId);
        Assert.Equal("Only the title changed", stored.Title);
        Assert.Equal("old description", stored.Description);
        Assert.Equal(evt.ScheduledTime, stored.ScheduledTime);
    }

    [Fact]
    public async Task GetEventById_EventWithDurationAndRoles_ReturnsThem()
    {
        var evt = TestEvents.Scheduled();
        evt.DurationMinutes = 90;
        evt.MentionRoleIds = new List<ulong> { 5, 6 };
        await StoreAsync(evt);

        var result = await _controller.GetEventById(evt.EventId);

        var detail = Assert.IsType<EventDetailDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(90, detail.DurationMinutes);
        Assert.Equal(new ulong[] { 5, 6 }, detail.MentionRoleIds);
    }

    [Fact]
    public async Task UpdateEvent_NewDurationAndRoles_AreStored()
    {
        var evt = await StoreAsync(TestEvents.Scheduled());

        var result = await _controller.UpdateEvent(evt.EventId, new UpdateEventRequest { DurationMinutes = 120, MentionRoleIds = new List<ulong> { 7, 8 } });

        Assert.IsType<NoContentResult>(result);
        var stored = await StoredAsync(evt.EventId);
        Assert.Equal(120, stored.DurationMinutes);
        Assert.Equal(new ulong[] { 7, 8 }, stored.MentionRoleIds);
    }

    [Fact]
    public async Task UpdateEvent_FormThatSendsTheCurrentDurationAndRolesBack_IsNotAChange()
    {
        // The web form always sends every field
        var evt = TestEvents.Scheduled();
        evt.DurationMinutes = 90;
        evt.MentionRoleIds = new List<ulong> { 5, 6 };
        await StoreAsync(evt);

        var result = await _controller.UpdateEvent(evt.EventId, new UpdateEventRequest { DurationMinutes = 90, MentionRoleIds = new List<ulong> { 6, 5 } });

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdateEvent_DurationOverADay_IsABadRequestAndNothingIsChanged()
    {
        var evt = await StoreAsync(TestEvents.Scheduled());

        var result = await _controller.UpdateEvent(evt.EventId, new UpdateEventRequest { DurationMinutes = 5000 });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null((await StoredAsync(evt.EventId)).DurationMinutes);
    }

    [Fact]
    public async Task UpdateEvent_NothingWouldChange_IsNotAnError()
    {
        var evt = await StoreAsync(TestEvents.Scheduled());

        var result = await _controller.UpdateEvent(evt.EventId, new UpdateEventRequest());

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task CancelEvent_RecurringEvent_IsABadRequestSoTheSeriesIsNotSilentlyEnded()
    {
        var evt = TestEvents.Scheduled();
        evt.IsRecurring = true;
        await StoreAsync(evt);

        var result = await _controller.CancelEvent(evt.EventId);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(EventStatus.Scheduled, (await StoredAsync(evt.EventId)).Status);
    }

    [Fact]
    public async Task CancelRecurringEventSeries_RecurringEvent_CancelsEveryLiveOccurrence()
    {
        var parent = TestEvents.Scheduled(1);
        parent.IsRecurring = true;
        var next = TestEvents.Scheduled(2);
        next.IsRecurring = true;
        next.ParentRecurringEventId = 1;
        await StoreAsync(parent);
        await StoreAsync(next);

        var result = await _controller.CancelRecurringEventSeries(2);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(EventStatus.Cancelled, (await StoredAsync(1)).Status);
        Assert.Equal(EventStatus.Cancelled, (await StoredAsync(2)).Status);
    }

    [Fact]
    public async Task CancelRecurringEventSeries_OneOffEvent_IsABadRequestAndTheEventIsUnchanged()
    {
        var evt = await StoreAsync(TestEvents.Scheduled());

        var result = await _controller.CancelRecurringEventSeries(evt.EventId);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(EventStatus.Scheduled, (await StoredAsync(evt.EventId)).Status);
    }

    [Theory]
    [MemberData(nameof(ChangingEndpoints))]
    public async Task ChangingEndpoint_EventInAGuildTheCallerDoesNotManage_IsNotFoundAndTheEventIsUntouched(string endpoint, Func<EventsController, ulong, Task<ActionResult>> call)
    {
        var foreign = await StoreAsync(TestEvents.Scheduled(900, guildId: OtherGuild));

        var result = await call(_controller, foreign.EventId);

        Assert.True(result is NotFoundObjectResult, $"{endpoint} returned {result.GetType().Name}");
        var stored = await StoredAsync(foreign.EventId);
        Assert.Equal((EventStatus.Scheduled, false, "Raid night"), (stored.Status, stored.IsDeleted, stored.Title));
    }

    [Theory]
    [MemberData(nameof(ChangingEndpoints))]
    public async Task ChangingEndpoint_DeletedOrMissingEvent_IsNotFound(string endpoint, Func<EventsController, ulong, Task<ActionResult>> call)
    {
        var deleted = await StoreAsync(TestEvents.Scheduled(901).Deleted());

        var forDeleted = await call(_controller, deleted.EventId);
        var forMissing = await call(_controller, 12345);

        Assert.True(forDeleted is NotFoundObjectResult, $"{endpoint} for a deleted event returned {forDeleted.GetType().Name}");
        Assert.True(forMissing is NotFoundObjectResult, $"{endpoint} for a missing event returned {forMissing.GetType().Name}");
    }

    [Fact]
    public async Task Endpoints_BotNotConnectedToDiscord_Answer503SoTheCallerKnowsToTryAgain()
    {
        var stored = await StoreAsync(TestEvents.Scheduled());
        var controller = NewController(ControllerTestContext.DisconnectedGuildAccess());

        var byGuild = await controller.GetGuildEvents(ManagedGuild);
        var byEvent = await controller.GetEventById(stored.EventId);
        var created = await controller.CreateEvent(ManagedGuild, NewEventRequest());

        Assert.Equal(503, Assert.IsType<ObjectResult>(byGuild.Result).StatusCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(byEvent.Result).StatusCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(created.Result).StatusCode);
    }
}
