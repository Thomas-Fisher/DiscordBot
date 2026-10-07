using DiscordBot.Application.Services;
using DiscordBot.Domain.Common;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using DiscordBot.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Unit.Application;

public class EventSchedulerTests
{
    private readonly FixedTimeProvider _clock = new();
    private readonly TestDb _db;
    private readonly RecordingActivityLogger _activity = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly EventScheduler _scheduler;

    public EventSchedulerTests()
    {
        _db = new TestDb(_clock);
        var settings = new SettingsService(_db.Guilds, _cache, NullLogger<SettingsService>.Instance);
        _scheduler = new EventScheduler(_db.Events, settings, _activity, _clock, NullLogger<EventScheduler>.Instance);
    }

    private async Task<Event> SeedAsync(ulong id, DateTime scheduledTime, RecurrencePattern? pattern = RecurrencePattern.Weekly, ulong? parentId = null)
    {
        var evt = TestEvents.Scheduled(id);
        evt.ScheduledTime = scheduledTime;
        evt.IsRecurring = pattern.HasValue;
        evt.RecurrencePattern = pattern;
        evt.ParentRecurringEventId = parentId;
        evt.ChannelId = null;
        evt.MessageId = null;
        evt.ReminderSent = true;
        _db.Context.Events.Add(evt);
        await _db.Context.SaveChangesAsync();

        return evt;
    }

    private async Task<List<Event>> StoredAsync()
    {
        await using var context = _db.Open();
        return await context.Events.AsNoTracking().OrderBy(e => e.ScheduledTime).ToListAsync();
    }

    private async Task SeedReminderAsync(ulong id, TimeSpan startsIn, int reminderMinutesBefore = 30, bool reminderSent = false, EventStatus status = EventStatus.Scheduled)
    {
        var evt = TestEvents.Scheduled(id);
        evt.ScheduledTime = _clock.UtcNow + startsIn;
        evt.ReminderMinutesBefore = reminderMinutesBefore;
        evt.ReminderSent = reminderSent;
        evt.Status = status;
        _db.Context.Events.Add(evt);
        await _db.Context.SaveChangesAsync();
    }

    private async Task<bool> ReminderSentAsync(ulong id)
    {
        await using var context = _db.Open();
        return (await context.Events.AsNoTracking().SingleAsync(e => e.EventId == id)).ReminderSent;
    }

    [Fact]
    public async Task RollForward_DueWeeklyEvent_IsCompletedAndReplacedByTheNextOccurrenceInTheFuture()
    {
        var due = _clock.UtcNow.AddDays(-10).AddMinutes(-5);
        await SeedAsync(1, due);

        var created = await _scheduler.RollForwardRecurringEventsAsync();

        var stored = await StoredAsync();
        var old = stored.Single(e => e.EventId == 1);
        var next = stored.Single(e => e.EventId != 1);
        Assert.Equal(next.EventId, Assert.Single(created).EventId);
        Assert.Equal(EventStatus.Completed, old.Status);
        Assert.Equal(EventStatus.Scheduled, next.Status);
        Assert.True(next.ScheduledTime > _clock.UtcNow, "the missed occurrences are skipped, not created one by one");
        Assert.True(next.ScheduledTime <= _clock.UtcNow.AddDays(7));
        Assert.Equal(due.TimeOfDay.TotalSeconds, next.ScheduledTime.TimeOfDay.TotalSeconds, precision: 3);   // the database keeps microseconds
        Assert.Equal(1UL, next.ParentRecurringEventId);
        Assert.False(next.ReminderSent);
        Assert.Contains(_activity.Entries, e => e.Action == "CreatedRecurring" && e.EntityId == next.EventId && e.Source == "Recurring");
    }

    [Fact]
    public async Task RollForward_GuildWithATimeZone_KeepsTheSameClockTimeThereWhateverTheDaylightSaving()
    {
        // 20:00 London time, ten days ago
        var london = GuildTimeZone.FindOrUtc("Europe/London");
        await new SettingsService(_db.Guilds, _cache, NullLogger<SettingsService>.Instance).SaveTimeZoneAsync(TestEvents.GuildId, london);
        var evening = london.ToLocal(_clock.UtcNow.AddDays(-10)).Date.AddHours(20);
        await SeedAsync(1, london.ToUtc(evening));

        await _scheduler.RollForwardRecurringEventsAsync();

        var next = (await StoredAsync()).Single(e => e.EventId != 1);
        Assert.Equal(TimeSpan.FromHours(20), london.ToLocal(next.ScheduledTime).TimeOfDay);
    }

    [Fact]
    public async Task RollForward_EventNotDueYet_IsLeftAlone()
    {
        await SeedAsync(1, _clock.UtcNow.AddDays(2));

        var created = await _scheduler.RollForwardRecurringEventsAsync();

        Assert.Empty(created);
        Assert.Equal(EventStatus.Scheduled, Assert.Single(await StoredAsync()).Status);
        Assert.Empty(_activity.Entries);
    }

    [Fact]
    public async Task RollForward_RunTwice_CreatesTheNextOccurrenceOnlyOnce()
    {
        await SeedAsync(1, _clock.UtcNow.AddDays(-1));

        await _scheduler.RollForwardRecurringEventsAsync();
        var second = await _scheduler.RollForwardRecurringEventsAsync();

        Assert.Empty(second);
        Assert.Equal(2, (await StoredAsync()).Count);
    }

    [Fact]
    public async Task RollForward_LaterOccurrenceOfASeries_LinksTheNewOneToTheOriginalEvent()
    {
        (await SeedAsync(1, _clock.UtcNow.AddDays(-15), parentId: null)).Status = EventStatus.Completed;
        var second = await SeedAsync(2, _clock.UtcNow.AddDays(-1), parentId: 1);

        await _scheduler.RollForwardRecurringEventsAsync();

        var created = (await StoredAsync()).Single(e => e.EventId != 1 && e.EventId != second.EventId);
        Assert.Equal(1UL, created.ParentRecurringEventId);
    }

    [Fact]
    public async Task RollForward_CancelledDeletedOrOneOffEvents_AreNotRolledForward()
    {
        var due = _clock.UtcNow.AddDays(-1);
        (await SeedAsync(1, due)).Status = EventStatus.Cancelled;
        (await SeedAsync(2, due)).IsDeleted = true;
        await SeedAsync(3, due, pattern: null);
        await _db.Context.SaveChangesAsync();

        await _scheduler.RollForwardRecurringEventsAsync();

        Assert.Equal(3, (await StoredAsync()).Count);
    }

    [Fact]
    public async Task ClaimDueReminders_EventInsideItsReminderWindow_IsClaimedAndReturned()
    {
        await SeedReminderAsync(1, TimeSpan.FromMinutes(20), reminderMinutesBefore: 30);

        var claimed = await _scheduler.ClaimDueRemindersAsync();

        Assert.Equal(1UL, Assert.Single(claimed).EventId);
        Assert.True(await ReminderSentAsync(1));
    }

    [Fact]
    public async Task ClaimDueReminders_EventStillOutsideItsReminderWindow_IsNotClaimed()
    {
        await SeedReminderAsync(1, TimeSpan.FromHours(2), reminderMinutesBefore: 30);

        var claimed = await _scheduler.ClaimDueRemindersAsync();

        Assert.Empty(claimed);
        Assert.False(await ReminderSentAsync(1));
    }

    [Fact]
    public async Task ClaimDueReminders_CancelledStartedOrAlreadySentEvents_AreNotClaimed()
    {
        await SeedReminderAsync(1, TimeSpan.FromMinutes(20), status: EventStatus.Cancelled);
        await SeedReminderAsync(2, TimeSpan.FromMinutes(-5));
        await SeedReminderAsync(3, TimeSpan.FromMinutes(20), reminderSent: true);

        var claimed = await _scheduler.ClaimDueRemindersAsync();

        Assert.Empty(claimed);
        Assert.False(await ReminderSentAsync(1));
        Assert.False(await ReminderSentAsync(2));
    }

    [Fact]
    public async Task ClaimDueReminders_RunTwice_HandsEachEventOutOnce()
    {
        await SeedReminderAsync(1, TimeSpan.FromMinutes(20));

        var first = await _scheduler.ClaimDueRemindersAsync();
        var second = await _scheduler.ClaimDueRemindersAsync();

        Assert.Single(first);
        Assert.Empty(second);
    }

    [Fact]
    public async Task GetUnannounced_ScheduledUpcomingEventWithAChannelButNoMessage_IsReturned()
    {
        var evt = await SeedAsync(1, _clock.UtcNow.AddDays(2), pattern: null);
        evt.ChannelId = 500;
        evt.MessageId = null;
        await _db.Context.SaveChangesAsync();

        var unannounced = await _scheduler.GetUnannouncedEventsAsync();

        Assert.Equal(1UL, Assert.Single(unannounced).EventId);
    }

    [Fact]
    public async Task GetUnannounced_EventsThatAreAnnouncedCancelledDeletedPastOrHaveNoChannel_AreNotReturned()
    {
        var announced = await SeedAsync(1, _clock.UtcNow.AddDays(2), pattern: null);
        announced.ChannelId = 500;
        announced.MessageId = 600;
        var cancelled = await SeedAsync(2, _clock.UtcNow.AddDays(2), pattern: null);
        cancelled.ChannelId = 500;
        cancelled.Status = EventStatus.Cancelled;
        var deleted = await SeedAsync(3, _clock.UtcNow.AddDays(2), pattern: null);
        deleted.ChannelId = 500;
        deleted.IsDeleted = true;
        var past = await SeedAsync(4, _clock.UtcNow.AddDays(-2), pattern: null);
        past.ChannelId = 500;
        await SeedAsync(5, _clock.UtcNow.AddDays(2), pattern: null);   // no channel at all
        await _db.Context.SaveChangesAsync();

        var unannounced = await _scheduler.GetUnannouncedEventsAsync();

        Assert.Empty(unannounced);
    }
}
