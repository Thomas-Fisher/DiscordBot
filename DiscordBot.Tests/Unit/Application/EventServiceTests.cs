using DiscordBot.Application.Errors;
using DiscordBot.Application.Models;
using DiscordBot.Application.Services;
using DiscordBot.Domain.Common;
using DiscordBot.Domain.Entities;
using DiscordBot.Tests.Support;
using Microsoft.EntityFrameworkCore;
using DiscordBot.Domain.Enums;

namespace DiscordBot.Tests.Unit.Application;

public class EventServiceTests
{
    private static readonly ActivityContext Actor = ActivityContext.Discord(42);

    private readonly FixedTimeProvider _time = new();
    private readonly TestDb _db;
    private readonly RecordingActivityLogger _activity = new();
    private readonly RecordingMessageUpdater _updater = new();
    private readonly FakeGuildDirectory _directory = new();
    private readonly EventService _service;

    public EventServiceTests()
    {
        _db = new TestDb(_time);
        _service = new EventService(_db.Events, _db.Guilds, _activity, _updater, _directory, _time);
    }

    private DateTime Later(int days = 1) => _time.UtcNow.AddDays(days);

    private async Task<Event> SavedEventAsync(ulong id = 1)
    {
        var evt = TestEvents.Scheduled(id);
        evt.ScheduledTime = Later(2);
        await _db.Events.CreateEventAsync(evt);
        return evt;
    }

    private async Task<Event> StoredAsync(ulong id)
    {
        using var context = _db.Open();
        return await context.Events.AsNoTracking().SingleAsync(e => e.EventId == id);
    }

    private async Task<int> StoredCountAsync()
    {
        using var context = _db.Open();
        return await context.Events.CountAsync();
    }

    [Fact]
    public async Task Create_ValidRequest_StoresATrimmedScheduledEventAndLogsIt()
    {
        var request = new NewEventRequest(10, 42, "  Raid night  ", "  bring flasks ", Later(), EventType.WowClassic);

        var result = await _service.CreateAsync(request, Actor);

        Assert.True(result.Success);
        var stored = await StoredAsync(result.Value!.EventId);
        Assert.Equal("Raid night", stored.Title);
        Assert.Equal("bring flasks", stored.Description);
        Assert.Equal(EventType.WowClassic, stored.Type);
        Assert.Equal(EventStatus.Scheduled, stored.Status);
        Assert.Single(_activity.Entries, e => e.Action == "Created" && e.UserId == 42);
    }

    [Fact]
    public async Task Create_WithDurationAndRolesToPing_StoresBothAndReadsThemBack()
    {
        var request = new NewEventRequest(10, 42, "Raid night", "", Later(), EventType.Generic, DurationMinutes: 150, MentionRoleIds: new ulong[] { 111, 222, 111 });

        var result = await _service.CreateAsync(request, Actor);

        var stored = await StoredAsync(result.Value!.EventId);
        Assert.Equal(150, stored.DurationMinutes);
        Assert.Equal(new ulong[] { 111, 222 }, stored.MentionRoleIds);
    }

    [Fact]
    public async Task Create_WithoutDurationOrRoles_StoresNoDurationAndNoRoles()
    {
        var request = new NewEventRequest(10, 42, "Raid night", "", Later(), EventType.Generic);

        var result = await _service.CreateAsync(request, Actor);

        var stored = await StoredAsync(result.Value!.EventId);
        Assert.Null(stored.DurationMinutes);
        Assert.Empty(stored.MentionRoleIds);
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        var soon = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var past = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        yield return new object[] { new NewEventRequest(10, 42, "ab", "", soon, EventType.Generic), EventValidationError.TitleTooShort };
        yield return new object[] { new NewEventRequest(10, 42, "Raid", new string('x', EventRules.MaxDescriptionLength + 1), soon, EventType.Generic), EventValidationError.DescriptionTooLong };
        yield return new object[] { new NewEventRequest(10, 42, "Raid", "", past, EventType.Generic), EventValidationError.DateMustBeInFuture };
        yield return new object[] { new NewEventRequest(10, 42, "Raid", "", soon, EventType.Generic, 0), EventValidationError.ReminderOutOfRange };
        yield return new object[] { new NewEventRequest(10, 42, "Raid", "", soon, EventType.Generic, DurationMinutes: 1441), EventValidationError.DurationOutOfRange };
        yield return new object[] { new NewEventRequest(10, 42, "Raid", "", soon, EventType.Generic, MentionRoleIds: Enumerable.Range(1, 26).Select(i => (ulong)i).ToList()), EventValidationError.TooManyMentionRoles };
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Create_InvalidRequest_ReturnsTheErrorAndStoresNothing(NewEventRequest request, EventValidationError expected)
    {
        // The clock stands at 2026-09-29 12:00 UTC, see FixedTimeProvider

        var result = await _service.CreateAsync(request, Actor);

        Assert.Equal(EventErrors.From(expected), result.Error);
        Assert.Null(result.Value);
        Assert.Equal(0, await StoredCountAsync());
    }

    [Fact]
    public async Task Update_ValidChanges_StoresTrimmedValuesLogsAndRefreshesTheMessage()
    {
        var evt = await SavedEventAsync();
        var newTime = Later(5);

        var updated = await _service.UpdateAsync(evt, new EventUpdate("  New title ", " New description ", newTime, 15), Actor);

        Assert.True(updated.Success);
        var stored = await StoredAsync(evt.EventId);
        Assert.Equal("New title", stored.Title);
        Assert.Equal("New description", stored.Description);
        Assert.Equal(newTime, stored.ScheduledTime);
        Assert.Equal(15, stored.ReminderMinutesBefore);
        Assert.Contains(evt.MessageId!.Value, _updater.Refreshed);
        Assert.Single(_activity.Entries, e => e.Action == "Updated");
    }

    [Fact]
    public async Task Update_OnlyTheTitle_LeavesEverythingElseAsItWas()
    {
        var evt = await SavedEventAsync();
        await _service.UpdateAsync(evt, new EventUpdate(DurationMinutes: 90, MentionRoleIds: new ulong[] { 5 }), Actor);
        var original = await StoredAsync(evt.EventId);

        await _service.UpdateAsync(evt, new EventUpdate(Title: "Renamed"), Actor);

        var stored = await StoredAsync(evt.EventId);
        Assert.Equal("Renamed", stored.Title);
        Assert.Equal(original.Description, stored.Description);
        Assert.Equal(original.ScheduledTime, stored.ScheduledTime);
        Assert.Equal(original.ReminderMinutesBefore, stored.ReminderMinutesBefore);
        Assert.Equal(original.DurationMinutes, stored.DurationMinutes);
        Assert.Equal(original.MentionRoleIds, stored.MentionRoleIds);
    }

    public static IEnumerable<object[]> ChangesAndWhetherTheReminderIsRearmed()
    {
        yield return new object[] { new EventUpdate(ScheduledTime: new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc)), true };
        yield return new object[] { new EventUpdate(ReminderMinutes: 10), true };
        yield return new object[] { new EventUpdate(Title: "Renamed"), false };
    }

    [Theory]
    [MemberData(nameof(ChangesAndWhetherTheReminderIsRearmed))]
    public async Task Update_AfterTheReminderWasSent_OnlyATimeOrReminderChangeSendsItAgain(EventUpdate update, bool rearmed)
    {
        var evt = await SavedEventAsync();

        await _service.UpdateAsync(evt, update, Actor);

        var stored = await StoredAsync(evt.EventId);
        Assert.Equal(!rearmed, stored.ReminderSent);
    }

    public static IEnumerable<object[]> InvalidUpdates()
    {
        yield return new object[] { new EventUpdate(Title: "ab"), EventValidationError.TitleTooShort };
        yield return new object[] { new EventUpdate(ScheduledTime: new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc)), EventValidationError.DateMustBeInFuture };
        yield return new object[] { new EventUpdate(ReminderMinutes: 20000), EventValidationError.ReminderOutOfRange };
        yield return new object[] { new EventUpdate(DurationMinutes: 1441), EventValidationError.DurationOutOfRange };
        // A valid title must not be saved when another field in the same edit is invalid
        yield return new object[] { new EventUpdate(Title: "Fine title", ReminderMinutes: 10081), EventValidationError.ReminderOutOfRange };
    }

    [Theory]
    [MemberData(nameof(InvalidUpdates))]
    public async Task Update_InvalidValue_ChangesNothing(EventUpdate update, EventValidationError expected)
    {
        var evt = await SavedEventAsync();
        var original = await StoredAsync(evt.EventId);

        var updated = await _service.UpdateAsync(evt, update, Actor);

        Assert.Equal(EventErrors.From(expected), updated.Error);
        var stored = await StoredAsync(evt.EventId);
        Assert.Equal((original.Title, original.Description, original.ScheduledTime, original.ReminderMinutesBefore),
            (stored.Title, stored.Description, stored.ScheduledTime, stored.ReminderMinutesBefore));
        Assert.Empty(_updater.Refreshed);
    }

    [Fact]
    public async Task Update_DurationAndRoles_AreStoredAndTheMessageIsRefreshed()
    {
        var evt = await SavedEventAsync();

        var updated = await _service.UpdateAsync(evt, new EventUpdate(DurationMinutes: 90, MentionRoleIds: new ulong[] { 5, 6 }), Actor);

        Assert.True(updated.Success);
        var stored = await StoredAsync(evt.EventId);
        Assert.Equal(90, stored.DurationMinutes);
        Assert.Equal(new ulong[] { 5, 6 }, stored.MentionRoleIds);
        Assert.Contains(evt.MessageId!.Value, _updater.Refreshed);
    }

    [Fact]
    public async Task Update_DurationOfZeroAndAnEmptyRoleList_RemoveTheDurationAndTheRoles()
    {
        var evt = await SavedEventAsync();
        await _service.UpdateAsync(evt, new EventUpdate(DurationMinutes: 90, MentionRoleIds: new ulong[] { 5 }), Actor);

        await _service.UpdateAsync(evt, new EventUpdate(DurationMinutes: 0, MentionRoleIds: Array.Empty<ulong>()), Actor);

        var stored = await StoredAsync(evt.EventId);
        Assert.Null(stored.DurationMinutes);
        Assert.Empty(stored.MentionRoleIds);
    }

    [Fact]
    public async Task Update_NothingSet_ReportsNothingToChange()
    {
        var evt = await SavedEventAsync();

        var updated = await _service.UpdateAsync(evt, new EventUpdate(), Actor);

        Assert.Equal(EventErrors.NothingToChange, updated.Error);
        Assert.Empty(_activity.Entries);
    }

    [Fact]
    public async Task Cancel_ScheduledEvent_IsStoredAsCancelledWithTheSameTimeAndRefreshed()
    {
        var evt = await SavedEventAsync();
        var originalTime = (await StoredAsync(evt.EventId)).ScheduledTime;

        await _service.CancelAsync(evt, ActivityContext.Api(7));

        var stored = await StoredAsync(evt.EventId);
        Assert.Equal(EventStatus.Cancelled, stored.Status);
        Assert.Equal(originalTime, stored.ScheduledTime);
        Assert.Contains(evt.MessageId!.Value, _updater.Refreshed);
        Assert.Single(_activity.Entries, e => e.Action == "Cancelled" && e.Source == "Api" && e.UserId == 7);
    }

    [Fact]
    public async Task Delete_LiveEvent_IsSoftDeleted()
    {
        var evt = await SavedEventAsync();

        await _service.DeleteAsync(evt, Actor);

        var stored = await StoredAsync(evt.EventId);
        Assert.True(stored.IsDeleted);
        Assert.Equal(_time.UtcNow, stored.DeletedAt);
    }

    [Fact]
    public async Task CancelSeries_MixedOccurrences_CancelsOnlyTheLiveScheduledOnesOfThatSeries()
    {
        var parent = TestEvents.Scheduled(1);
        parent.IsRecurring = true;
        var next = TestEvents.Scheduled(2);
        next.IsRecurring = true;
        next.ParentRecurringEventId = 1;
        var completed = TestEvents.Scheduled(3).WithStatus(EventStatus.Completed);
        completed.ParentRecurringEventId = 1;
        var deleted = TestEvents.Scheduled(4).Deleted();
        deleted.ParentRecurringEventId = 1;
        var otherSeries = TestEvents.Scheduled(5);
        otherSeries.ParentRecurringEventId = 99;
        foreach (var evt in new[] { parent, next, completed, deleted, otherSeries })
        {
            await _db.Events.CreateEventAsync(evt);
        }

        var cancelled = await _service.CancelSeriesAsync(next, Actor);

        Assert.Equal(2, cancelled);
        Assert.Equal(EventStatus.Cancelled, (await StoredAsync(1)).Status);
        Assert.Equal(EventStatus.Cancelled, (await StoredAsync(2)).Status);
        Assert.False((await StoredAsync(2)).IsRecurring);
        Assert.Equal(EventStatus.Completed, (await StoredAsync(3)).Status);
        Assert.Equal(EventStatus.Scheduled, (await StoredAsync(5)).Status);
    }

    [Fact]
    public async Task CompleteEndedEvents_MixedEvents_CompletesOnlyTheOneOffsThatHaveEnded()
    {
        var endedNoDuration = TestEvents.Scheduled(1);
        endedNoDuration.ScheduledTime = _time.UtcNow.AddHours(-1);
        var endedWithDuration = TestEvents.Scheduled(2);
        endedWithDuration.ScheduledTime = _time.UtcNow.AddHours(-3);
        endedWithDuration.DurationMinutes = 60;
        var stillRunning = TestEvents.Scheduled(3);
        stillRunning.ScheduledTime = _time.UtcNow.AddHours(-1);
        stillRunning.DurationMinutes = 180;
        var upcoming = TestEvents.Scheduled(4);
        upcoming.ScheduledTime = Later();
        var recurring = TestEvents.Scheduled(5);
        recurring.ScheduledTime = _time.UtcNow.AddHours(-1);
        recurring.IsRecurring = true;
        var cancelled = TestEvents.Scheduled(6).WithStatus(EventStatus.Cancelled);
        cancelled.ScheduledTime = _time.UtcNow.AddHours(-1);
        foreach (var evt in new[] { endedNoDuration, endedWithDuration, stillRunning, upcoming, recurring, cancelled })
        {
            await _db.Events.CreateEventAsync(evt);
        }

        var completed = await _service.CompleteEndedEventsAsync();

        Assert.Equal(2, completed);
        Assert.Equal(EventStatus.Completed, (await StoredAsync(1)).Status);
        Assert.Equal(EventStatus.Completed, (await StoredAsync(2)).Status);
        Assert.Equal(EventStatus.Scheduled, (await StoredAsync(3)).Status);
        Assert.Equal(EventStatus.Scheduled, (await StoredAsync(4)).Status);
        Assert.Equal(EventStatus.Scheduled, (await StoredAsync(5)).Status);
        Assert.Equal(EventStatus.Cancelled, (await StoredAsync(6)).Status);
        Assert.Equal(2, _activity.Entries.Count(e => e.Action == "Completed" && e.Source == "System"));
    }

    [Fact]
    public async Task CompleteEndedEvents_NothingHasEnded_ChangesNothing()
    {
        await SavedEventAsync();

        var completed = await _service.CompleteEndedEventsAsync();

        Assert.Equal(0, completed);
        Assert.Empty(_activity.Entries);
    }

    [Fact]
    public async Task Cancel_AlreadyCancelledEvent_IsRefusedAndNothingIsLoggedOrRefreshed()
    {
        var evt = await SavedEventAsync();
        await _service.CancelAsync(evt, Actor);
        _activity.Entries.Clear();
        _updater.Refreshed.Clear();

        var second = await _service.CancelAsync(evt, Actor);

        Assert.Equal(EventErrors.NotChangeable, second.Error);
        Assert.Empty(_activity.Entries);
        Assert.Empty(_updater.Refreshed);
    }

    [Fact]
    public async Task Update_CancelledOrFinishedEvent_IsRefusedAndNothingChanges()
    {
        var cancelled = await SavedEventAsync(1);
        await _service.CancelAsync(cancelled, Actor);
        var finished = TestEvents.Scheduled(2).WithStatus(EventStatus.Completed);
        await _db.Events.CreateEventAsync(finished);

        var onCancelled = await _service.UpdateAsync(cancelled, new EventUpdate(Title: "A new title"), Actor);
        var onFinished = await _service.UpdateAsync(finished, new EventUpdate(Title: "A new title"), Actor);

        Assert.Equal(EventErrors.NotChangeable, onCancelled.Error);
        Assert.Equal(EventErrors.NotChangeable, onFinished.Error);
        Assert.Equal("Raid night", (await StoredAsync(1)).Title);
    }

    [Fact]
    public async Task Delete_EventWithAnAnnouncement_AlsoRemovesTheAnnouncementFromDiscord()
    {
        var evt = await SavedEventAsync();

        var deleted = await _service.DeleteAsync(evt, Actor);

        Assert.True(deleted.Success);
        Assert.Equal((evt.ChannelId!.Value, evt.MessageId!.Value), Assert.Single(_updater.Removed));
    }

    [Fact]
    public async Task Delete_EventThatWasAlreadyDeleted_IsRefused()
    {
        var evt = await SavedEventAsync();
        await _service.DeleteAsync(evt, Actor);
        _updater.Removed.Clear();

        var second = await _service.DeleteAsync(evt, Actor);

        Assert.Equal(EventErrors.NotFound, second.Error);
        Assert.Empty(_updater.Removed);
    }

    [Fact]
    public async Task Create_RoleThatIsNotInTheServer_IsRefusedAndNothingIsStored()
    {
        _directory.Add(TestGuilds.Basic(10), roles: [new GuildRole(5, "Raider", 1)]);
        var request = new NewEventRequest(10, 42, "Raid night", "", Later(), EventType.Generic, MentionRoleIds: new ulong[] { 5, 999 });

        var result = await _service.CreateAsync(request, Actor);

        Assert.Equal(EventErrors.UnknownRole, result.Error);
        Assert.Equal(0, await StoredCountAsync());
    }

    [Fact]
    public async Task Create_RolesThatAreInTheServer_AreAccepted()
    {
        _directory.Add(TestGuilds.Basic(10), roles: [new GuildRole(5, "Raider", 1), new GuildRole(6, "Healer", 2)]);
        var request = new NewEventRequest(10, 42, "Raid night", "", Later(), EventType.Generic, MentionRoleIds: new ulong[] { 5, 6 });

        var result = await _service.CreateAsync(request, Actor);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Update_RoleThatIsNotInTheServer_IsRefused()
    {
        _directory.Add(TestGuilds.Basic(10), roles: [new GuildRole(5, "Raider", 1)]);
        var evt = await SavedEventAsync();

        var result = await _service.UpdateAsync(evt, new EventUpdate(MentionRoleIds: new ulong[] { 999 }), Actor);

        Assert.Equal(EventErrors.UnknownRole, result.Error);
    }
}
