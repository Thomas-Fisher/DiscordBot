using DiscordBot.Domain.Entities;
using DiscordBot.Tests.Support;
using DiscordBot.Domain.Enums;

namespace DiscordBot.Tests.Unit.Domain;

public class EventOccurrenceTests
{
    private static Event Series()
    {
        var evt = TestEvents.Scheduled(10, EventType.WowClassic);
        evt.IsRecurring = true;
        evt.RecurrencePattern = RecurrencePattern.EveryFriday;
        evt.DurationMinutes = 120;
        evt.MentionRoleIds = new List<ulong> { 5, 6 };
        evt.ReminderMinutesBefore = 45;
        evt.ReminderSent = true;
        evt.Status = EventStatus.Completed;
        return evt;
    }

    [Fact]
    public void CreateNextOccurrence_FirstOccurrenceOfASeries_BecomesTheParentOfTheNext()
    {
        var first = Series();
        var when = DateTime.UtcNow.AddDays(7);

        var next = first.CreateNextOccurrence(11, when, DateTime.UtcNow);

        Assert.Equal((11UL, 10UL), (next.EventId, next.ParentRecurringEventId!.Value));
        Assert.Equal(when, next.ScheduledTime);
    }

    [Fact]
    public void CreateNextOccurrence_LaterOccurrence_KeepsTheOriginalParent()
    {
        var later = Series();
        later.ParentRecurringEventId = 3;

        var next = later.CreateNextOccurrence(11, DateTime.UtcNow.AddDays(7), DateTime.UtcNow);

        Assert.Equal(3UL, next.ParentRecurringEventId);
    }

    [Fact]
    public void CreateNextOccurrence_CopiesTheSettingsAndStartsFresh()
    {
        var first = Series();

        var next = first.CreateNextOccurrence(11, DateTime.UtcNow.AddDays(7), DateTime.UtcNow);

        // What makes it the same event...
        Assert.Equal((first.Title, first.GuildId, first.ChannelId, first.Type, first.DurationMinutes, first.ReminderMinutesBefore),
            (next.Title, next.GuildId, next.ChannelId, next.Type, next.DurationMinutes, next.ReminderMinutesBefore));
        Assert.Equal(first.MentionRoleIds, next.MentionRoleIds);
        Assert.NotSame(first.MentionRoleIds, next.MentionRoleIds);
        Assert.True(next.IsRecurring);
        Assert.Equal(RecurrencePattern.EveryFriday, next.RecurrencePattern);

        // ...and what must not carry over
        Assert.Equal(EventStatus.Scheduled, next.Status);
        Assert.False(next.ReminderSent);
        Assert.Null(next.MessageId);
        Assert.Empty(next.Signups);
    }
}
