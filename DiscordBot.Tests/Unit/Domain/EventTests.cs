using DiscordBot.Domain.Common;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;

namespace DiscordBot.Tests.Unit.Domain;

public class EventTests
{
    private static readonly DateTime Now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Now.AddDays(3);

    private static Event NewEvent() => Event.Schedule(1, 10, 42, "Raid night", "Bring flasks", Later, EventType.Generic, Now);

    [Fact]
    public void Schedule_TitleAndDescriptionWithSpaces_AreStoredTrimmedAndRolesOnceEach()
    {
        var evt = Event.Schedule(1, 10, 42, "  Raid night ", " Bring flasks ", Later, EventType.Generic, Now, mentionRoleIds: new ulong[] { 5, 5, 6 });

        Assert.Equal(("Raid night", "Bring flasks"), (evt.Title, evt.Description));
        Assert.Equal(new ulong[] { 5, 6 }, evt.MentionRoleIds);
        Assert.Equal((EventStatus.Scheduled, Now, false), (evt.Status, evt.CreatedAt, evt.IsRecurring));
    }

    [Fact]
    public void ApplyChanges_ValidValues_ChangesThemAndRearmsTheReminderWhenTheTimeMoves()
    {
        var evt = NewEvent();
        evt.MarkReminderSent(Now);
        var newTime = Later.AddHours(2);

        var error = evt.ApplyChanges(new EventUpdate(Title: "  New title ", ScheduledTime: newTime, DurationMinutes: 90), Now.AddMinutes(5));

        Assert.Null(error);
        Assert.Equal(("New title", newTime, 90), (evt.Title, evt.ScheduledTime, evt.DurationMinutes));
        Assert.False(evt.ReminderSent);
        Assert.Equal(Now.AddMinutes(5), evt.UpdatedAt);
    }

    [Fact]
    public void ApplyChanges_ReminderOfZero_RemovesTheReminder()
    {
        var evt = NewEvent();
        evt.ChangeReminder(30, Now);

        var error = evt.ApplyChanges(new EventUpdate(ReminderMinutes: 0), Now);

        Assert.Null(error);
        Assert.Null(evt.ReminderMinutesBefore);
    }

    [Fact]
    public void ApplyChanges_NewReminder_SetsItAndRearmsIt()
    {
        var evt = NewEvent();
        evt.MarkReminderSent(Now);

        var error = evt.ApplyChanges(new EventUpdate(ReminderMinutes: 1440), Now);

        Assert.Null(error);
        Assert.Equal((1440, false), (evt.ReminderMinutesBefore, evt.ReminderSent));
    }

    [Fact]
    public void ApplyChanges_OneGoodValueAndOneBadOne_ChangesNothingAtAll()
    {
        var evt = NewEvent();

        var error = evt.ApplyChanges(new EventUpdate(Title: "A fine new title", ReminderMinutes: 20000), Now);

        Assert.Equal(EventValidationError.ReminderOutOfRange, error);
        Assert.Equal("Raid night", evt.Title);
        Assert.Null(evt.UpdatedAt);
    }

    [Fact]
    public void ApplyChanges_NothingSet_SaysThereIsNothingToChange()
    {
        Assert.Equal(EventValidationError.NothingToChange, NewEvent().ApplyChanges(new EventUpdate(), Now));
    }

    [Fact]
    public void ApplyChanges_DurationOfZero_RemovesTheDuration()
    {
        var evt = NewEvent();
        evt.ApplyChanges(new EventUpdate(DurationMinutes: 60), Now);

        var error = evt.ApplyChanges(new EventUpdate(DurationMinutes: 0), Now);

        Assert.Null(error);
        Assert.Null(evt.DurationMinutes);
    }

    [Fact]
    public void ApplyChanges_TimeInThePast_IsRefused()
    {
        Assert.Equal(EventValidationError.DateMustBeInFuture, NewEvent().ApplyChanges(new EventUpdate(ScheduledTime: Now.AddMinutes(-1)), Now));
    }
}
