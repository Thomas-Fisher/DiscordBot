using DiscordBot.Application.Errors;
using DiscordBot.Application.Models;
using DiscordBot.Application.Services;
using DiscordBot.Domain.Enums;
using DiscordBot.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Unit.Application;

public class EventCreatorTests
{
    private const ulong Channel = 55;

    private static readonly ActivityContext Actor = ActivityContext.Discord(42);

    private readonly FixedTimeProvider _clock = new();
    private readonly TestDb _db;
    private readonly FakeEventAnnouncer _announcer = new();
    private readonly RecordingMessageUpdater _updater = new();
    private readonly EventCreator _creator;

    public EventCreatorTests()
    {
        _db = new TestDb(_clock);
        var service = new EventService(_db.Events, _db.Guilds, new RecordingActivityLogger(), _updater, new FakeGuildDirectory(), _clock);
        _creator = new EventCreator(service, _announcer, NullLogger<EventCreator>.Instance);
    }

    private NewEventRequest Request(DateTime? scheduledTime = null) =>
        new(TestEvents.GuildId, 42, "Raid night", "Bring flasks", scheduledTime ?? _clock.UtcNow.AddDays(1), EventType.Generic);

    [Fact]
    public async Task CreateAndAnnounce_ValidRequest_LinksTheStoredEventToTheMessageThatWasPosted()
    {
        var result = await _creator.CreateAndAnnounceAsync(Request(), Channel, Actor);

        Assert.True(result.Success);
        var stored = await _db.Context.Events.AsNoTracking().SingleAsync();
        Assert.Equal((Channel, result.Value!.EventId + 1), (stored.ChannelId, stored.MessageId));
        Assert.Equal((stored.EventId, Channel), Assert.Single(_announcer.Posted));
    }

    [Fact]
    public async Task CreateAndAnnounce_RequestThatBreaksARule_PostsNothingAndStoresNothing()
    {
        var result = await _creator.CreateAndAnnounceAsync(Request(_clock.UtcNow.AddMinutes(-5)), Channel, Actor);

        Assert.Equal(EventErrors.DateMustBeInFuture, result.Error);
        Assert.Empty(_announcer.Posted);
        Assert.Empty(_db.Context.Events);
    }

    [Fact]
    public async Task CreateAndAnnounce_AnnouncementCannotBePosted_DeletesTheEventAgainSoNothingIsLeftBehindThatNobodyCanSee()
    {
        _announcer.Fail = true;

        var result = await _creator.CreateAndAnnounceAsync(Request(), Channel, Actor);

        Assert.Equal(EventErrors.AnnouncementFailed, result.Error);
        var stored = await _db.Context.Events.AsNoTracking().SingleAsync();
        Assert.True(stored.IsDeleted);
        Assert.Null(stored.MessageId);
        Assert.Empty(_updater.Removed);
    }
}
