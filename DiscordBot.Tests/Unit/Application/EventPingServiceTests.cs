using DiscordBot.Application.Errors;
using DiscordBot.Application.Models;
using DiscordBot.Application.Services;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using DiscordBot.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Unit.Application;

public class EventPingServiceTests
{
    private const ulong Guild = TestEvents.GuildId;
    private const ulong RaidersRole = 11;
    private const ulong OfficersRole = 12;

    private static readonly ActivityContext Admin = ActivityContext.Api(7);

    private readonly TestDb _db = new();
    private readonly FakeGuildDirectory _directory = new();
    private readonly FakePingSender _sender = new();
    private readonly RecordingActivityLogger _activity = new();
    private readonly FixedTimeProvider _clock = new();
    private readonly EventPingService _service;

    public EventPingServiceTests()
    {
        _directory.Add(TestGuilds.Basic(Guild), roles: [new GuildRole(RaidersRole, "Raiders", 2), new GuildRole(OfficersRole, "Officers", 1)]);
        _directory.RoleMembers[(Guild, RaidersRole)] = new HashSet<ulong> { 1, 2, 3, 4, 5, 6 };
        _directory.RoleMembers[(Guild, OfficersRole)] = new HashSet<ulong> { 6, 7 };
        _service = new EventPingService(_db.Signups, _directory, _sender, _activity, new MemoryCache(new MemoryCacheOptions()), _clock, NullLogger<EventPingService>.Instance);
    }

    private async Task<Event> StoredEventAsync(Action<Event>? change = null)
    {
        var evt = TestEvents.Wow();
        evt.MentionRoleIds = [RaidersRole];
        change?.Invoke(evt);
        await _db.Events.CreateEventAsync(evt);

        return evt;
    }

    private async Task SignUpAsync(Event evt, ulong user, int? group = null)
    {
        await _db.Signups.SignUpAsync(evt.EventId, user, "mage", "fire");
        if (group != null)
        {
            var current = _db.Context.EventSignups.Where(s => s.EventId == evt.EventId && s.GroupNumber != null).ToDictionary(s => s.UserId, s => s.GroupNumber!.Value);
            current[user] = group.Value;
            await _db.Signups.SetRosterAsync(evt.EventId, current);
        }
    }

    private static IEnumerable<ulong> Users(IEnumerable<PingRecipient> recipients) => recipients.Select(r => r.UserId);

    [Fact]
    public async Task Send_Roster_PingsEveryoneInAGroupInGroupOrderWithTheirGroupAndTheTrimmedNote()
    {
        var evt = await StoredEventAsync();
        await SignUpAsync(evt, 1, group: 2);
        await SignUpAsync(evt, 2, group: 1);
        await SignUpAsync(evt, 3);

        var result = await _service.SendAsync(evt, new PingRequest(PingTarget.Roster, Note: "  Bring flasks  "), Admin);

        Assert.Equal(2, result.Value);
        var sent = Assert.Single(_sender.Sent);
        Assert.Equal((evt.EventId, PingTarget.Roster, "Bring flasks"), (sent.EventId, sent.Target, sent.Note));
        Assert.Equal(new[] { (2UL, 1), (1UL, 2) }, sent.Recipients.Select(r => (r.UserId, r.Group!.Value)));
        Assert.All(sent.Recipients, r => Assert.Equal(("mage", "fire", false), (r.WowClass, r.WowSpec, r.IsUnsure)));
    }

    [Fact]
    public async Task Send_Roster_AlsoPingsAnUnsurePersonWhoWasPutInAGroup()
    {
        var evt = await StoredEventAsync();
        await SignUpAsync(evt, 1, group: 1);
        await _db.Signups.MarkUnsureAsync(evt.EventId, 2, compactOrder: false);
        await _db.Signups.SetRosterAsync(evt.EventId, new Dictionary<ulong, int> { [1] = 1, [2] = 1 });

        await _service.SendAsync(evt, new PingRequest(PingTarget.Roster), Admin);

        Assert.Equal(new ulong[] { 1, 2 }, Users(Assert.Single(_sender.Sent).Recipients));
    }

    [Fact]
    public async Task Send_Standby_PingsOnlyThoseSignedUpWithNoGroup()
    {
        var evt = await StoredEventAsync();
        await SignUpAsync(evt, 1, group: 1);
        await SignUpAsync(evt, 2);
        await SignUpAsync(evt, 3);

        await _service.SendAsync(evt, new PingRequest(PingTarget.Standby), Admin);

        Assert.Equal(new ulong[] { 2, 3 }, Users(Assert.Single(_sender.Sent).Recipients));
    }

    [Fact]
    public async Task Send_NoResponse_PingsMembersOfTheEventsRolesWhoHaveNotAnswered()
    {
        var evt = await StoredEventAsync();
        await SignUpAsync(evt, 1);
        await _db.Signups.SignUpAsync(evt.EventId, 2, "mage", "fire");
        await _db.Signups.SignOffAsync(evt.EventId, 2, compactOrder: false);
        await _db.Signups.MarkUnsureAsync(evt.EventId, 3, compactOrder: false);

        await _service.SendAsync(evt, new PingRequest(PingTarget.NoResponse), Admin);

        Assert.Equal(new ulong[] { 4, 5, 6 }, Users(Assert.Single(_sender.Sent).Recipients));
    }

    [Fact]
    public async Task Send_NoResponseIncludingUnsure_AlsoPingsThePeopleWhoAnsweredUnsure()
    {
        var evt = await StoredEventAsync();
        await SignUpAsync(evt, 1);
        await _db.Signups.MarkUnsureAsync(evt.EventId, 3, compactOrder: false);

        await _service.SendAsync(evt, new PingRequest(PingTarget.NoResponse, IncludeUnsure: true), Admin);

        Assert.Equal(new ulong[] { 2, 3, 4, 5, 6 }, Users(Assert.Single(_sender.Sent).Recipients));
    }

    [Fact]
    public async Task Send_NoResponseWithRolesChosen_UsesThoseRolesInsteadOfTheEventsOwn()
    {
        var evt = await StoredEventAsync();

        await _service.SendAsync(evt, new PingRequest(PingTarget.NoResponse, RoleIds: [OfficersRole]), Admin);

        Assert.Equal(new ulong[] { 6, 7 }, Users(Assert.Single(_sender.Sent).Recipients));
    }

    [Fact]
    public async Task Send_NoResponseWithNoRolesAnywhere_AsksForARoleAndPingsNobody()
    {
        var evt = await StoredEventAsync(e => e.MentionRoleIds = []);

        var result = await _service.SendAsync(evt, new PingRequest(PingTarget.NoResponse), Admin);

        Assert.Equal(PingErrors.NeedsRoles, result.Error);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Send_NoResponseWithARoleThatIsNotInTheServer_IsRefused()
    {
        var evt = await StoredEventAsync();

        var result = await _service.SendAsync(evt, new PingRequest(PingTarget.NoResponse, RoleIds: [999]), Admin);

        Assert.Equal(PingErrors.UnknownRole, result.Error);
    }

    [Fact]
    public async Task Send_NoOneToPing_IsRefusedWithoutPostingAnything()
    {
        var evt = await StoredEventAsync();

        var result = await _service.SendAsync(evt, new PingRequest(PingTarget.Standby), Admin);

        Assert.Equal(PingErrors.NoOneToPing, result.Error);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Send_NoteLongerThanTheLimit_IsRefused()
    {
        var evt = await StoredEventAsync();
        await SignUpAsync(evt, 1, group: 1);

        var result = await _service.SendAsync(evt, new PingRequest(PingTarget.Roster, Note: new string('x', PingErrors.MaxNoteLength + 1)), Admin);

        Assert.Equal(PingErrors.NoteTooLong, result.Error);
    }

    [Fact]
    public async Task Send_EventWithNoAnnouncementOrThatIsNotOpen_IsRefused()
    {
        var unannounced = await StoredEventAsync(e => e.MessageId = null);
        var cancelled = TestEvents.Wow(2).WithStatus(EventStatus.Cancelled);
        await _db.Events.CreateEventAsync(cancelled);

        Assert.Equal(PingErrors.NotAnnounced, (await _service.SendAsync(unannounced, new PingRequest(PingTarget.Roster), Admin)).Error);
        Assert.Equal(PingErrors.EventNotOpen, (await _service.SendAsync(cancelled, new PingRequest(PingTarget.Roster), Admin)).Error);
    }

    [Fact]
    public async Task Send_PostingFails_ReportsItAndRecordsNothing()
    {
        var evt = await StoredEventAsync();
        await SignUpAsync(evt, 1, group: 1);
        _sender.Fail = true;

        var result = await _service.SendAsync(evt, new PingRequest(PingTarget.Roster), Admin);

        Assert.Equal(PingErrors.SendFailed, result.Error);
        Assert.DoesNotContain(_activity.Entries, e => e.Action == "PingSent");
        Assert.Null((await _service.PreviewAsync(evt, new PingRequest(PingTarget.Roster))).Value!.RecentlyPingedAt);
    }

    [Fact]
    public async Task Send_Success_IsLoggedWithWhoDidItAndHowManyWerePinged()
    {
        var evt = await StoredEventAsync();
        await SignUpAsync(evt, 1, group: 1);

        await _service.SendAsync(evt, new PingRequest(PingTarget.Roster), Admin);

        Assert.Contains(_activity.Entries, e => e.Action == "PingSent" && e.EntityId == evt.EventId && e.UserId == 7);
    }

    [Fact]
    public async Task Preview_ListsTheRecipientsWithoutPostingAndWarnsAboutAPingJustSent()
    {
        var evt = await StoredEventAsync();
        await SignUpAsync(evt, 1, group: 1);

        var before = await _service.PreviewAsync(evt, new PingRequest(PingTarget.Roster));
        await _service.SendAsync(evt, new PingRequest(PingTarget.Roster), Admin);
        var after = await _service.PreviewAsync(evt, new PingRequest(PingTarget.Roster));
        var otherTarget = await _service.PreviewAsync(evt, new PingRequest(PingTarget.Standby));

        Assert.Equal(new ulong[] { 1 }, Users(before.Value!.Recipients));
        Assert.Null(before.Value.RecentlyPingedAt);
        Assert.Single(_sender.Sent);
        Assert.Equal(_clock.UtcNow, after.Value!.RecentlyPingedAt);
        Assert.Null(otherTarget.Value!.RecentlyPingedAt);
    }
}
