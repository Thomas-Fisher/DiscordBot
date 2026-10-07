using DiscordBot.Domain.Entities;
using DiscordBot.Tests.Support;
using DiscordBot.Domain.Enums;

namespace DiscordBot.Tests.Unit.Persistence;

public class EventRepositoryTests
{
    private const ulong GuildId = 10;

    private readonly TestDb _db = new();

    private static Event Event(ulong id, DateTime when, EventStatus status = EventStatus.Scheduled, bool deleted = false,
        bool reminded = false, bool recurring = false, ulong guild = GuildId) => new()
        {
            EventId = id,
            GuildId = guild,
            Title = "e",
            ScheduledTime = when,
            Status = status,
            IsDeleted = deleted,
            ReminderSent = reminded,
            ReminderMinutesBefore = 30,
            IsRecurring = recurring
        };

    private async Task StoreAsync(params Event[] events)
    {
        _db.Context.Events.AddRange(events);
        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetGuildEventCounts_MixedEvents_CountsOnlyLiveEventsOfThatGuild()
    {
        var now = DateTime.UtcNow;
        await StoreAsync(
            Event(1, now.AddDays(1)),
            Event(2, now.AddDays(2)),
            Event(3, now.AddDays(-1), EventStatus.Completed),
            Event(4, now.AddDays(3), EventStatus.Cancelled),
            Event(5, now.AddDays(4), deleted: true),
            Event(6, now.AddDays(1), guild: 99));

        var counts = await _db.Events.GetGuildEventCountsAsync(GuildId);

        Assert.Equal((3, 2), (counts.Total, counts.Upcoming));
    }

    [Fact]
    public async Task GetEventsDueForReminder_MixedEvents_ReturnsOnlyLiveUnremindedEventsInsideTheLookahead()
    {
        var now = DateTime.UtcNow;
        await StoreAsync(
            Event(1, now.AddMinutes(20)),                                // due
            Event(2, now.AddHours(23)),                                  // inside the 24h lookahead
            Event(3, now.AddHours(30)),                                  // beyond the lookahead
            Event(4, now.AddMinutes(-5)),                                // already started
            Event(5, now.AddMinutes(20), EventStatus.Cancelled),
            Event(6, now.AddMinutes(20), deleted: true),
            Event(7, now.AddMinutes(20), reminded: true));

        var due = await _db.Events.GetEventsDueForReminderAsync(now, TimeSpan.FromHours(24));

        Assert.Equal(new ulong[] { 1, 2 }, due.Select(e => e.EventId));
    }

    [Fact]
    public async Task GetDueRecurringEvents_MixedEvents_ReturnsOnlyPastScheduledRecurringOnes()
    {
        var now = DateTime.UtcNow;
        await StoreAsync(
            Event(1, now.AddMinutes(-10), recurring: true),
            Event(2, now.AddDays(1), recurring: true),                                       // not yet
            Event(3, now.AddMinutes(-10), recurring: false),                                 // one-off
            Event(4, now.AddMinutes(-10), EventStatus.Completed, recurring: true),
            Event(5, now.AddMinutes(-10), recurring: true, deleted: true));

        var due = await _db.Events.GetDueRecurringEventsAsync();

        Assert.Equal(new ulong[] { 1 }, due.Select(e => e.EventId));
    }

    private static DiscordBot.Infrastructure.Persistence.Repositories.EventRepository RepositoryOver(DiscordBot.Infrastructure.Persistence.AppDbContext context)
    {
        return new DiscordBot.Infrastructure.Persistence.Repositories.EventRepository(context, TimeProvider.System);
    }

    [Fact]
    public async Task UpdateEvent_StaleCopyThatChangesOneField_DoesNotOverwriteOtherFieldsChangedMeanwhile()
    {
        // A worker loads the event, and while it works someone else renames it
        await StoreAsync(Event(1, DateTime.UtcNow.AddDays(1)));
        using var workerContext = _db.Open();
        var workerRepository = RepositoryOver(workerContext);
        var stale = (await workerRepository.GetEventByIdAsync(1))!;

        using (var editorContext = _db.Open())
        {
            var edited = editorContext.Events.Single(e => e.EventId == 1);
            edited.Title = "Renamed by the editor";
            await editorContext.SaveChangesAsync();
        }

        stale.ChangeDuration(90, DateTime.UtcNow);
        await workerRepository.UpdateEventAsync(stale);

        using var check = _db.Open();
        var stored = check.Events.Single(e => e.EventId == 1);
        Assert.Equal(("Renamed by the editor", 90), (stored.Title, stored.DurationMinutes));
    }

    [Fact]
    public async Task TryClaimReminder_TwoWorkersHoldingTheSameUnsentEvent_OnlyTheFirstMayClaimIt()
    {
        await StoreAsync(Event(1, DateTime.UtcNow.AddMinutes(10)));
        using var contextA = _db.Open();
        using var contextB = _db.Open();
        var repositoryA = RepositoryOver(contextA);
        var repositoryB = RepositoryOver(contextB);
        var seenByA = (await repositoryA.GetEventByIdAsync(1))!;
        var seenByB = (await repositoryB.GetEventByIdAsync(1))!;

        var claimedByA = await repositoryA.TryClaimReminderAsync(seenByA);
        var claimedByB = await repositoryB.TryClaimReminderAsync(seenByB);

        Assert.True(claimedByA);
        Assert.False(claimedByB);
    }

    [Fact]
    public async Task CompleteRecurringEventAndCreateNext_TwoWorkersRollTheSameOccurrence_OnlyTheFirstSucceeds()
    {
        await StoreAsync(Event(1, DateTime.UtcNow.AddMinutes(-5), recurring: true));
        using var contextA = _db.Open();
        using var contextB = _db.Open();
        var repositoryA = RepositoryOver(contextA);
        var repositoryB = RepositoryOver(contextB);
        var dueForA = (await repositoryA.GetEventByIdAsync(1))!;
        var dueForB = (await repositoryB.GetEventByIdAsync(1))!;

        var rolledByA = await repositoryA.CompleteRecurringEventAndCreateNextAsync(dueForA, Event(2, DateTime.UtcNow.AddDays(7), recurring: true));
        var rolledByB = await repositoryB.CompleteRecurringEventAndCreateNextAsync(dueForB, Event(3, DateTime.UtcNow.AddDays(7), recurring: true));

        Assert.True(rolledByA);
        Assert.False(rolledByB);
        using var check = _db.Open();
        Assert.Equal(EventStatus.Completed, check.Events.Single(e => e.EventId == 1).Status);
    }
}
