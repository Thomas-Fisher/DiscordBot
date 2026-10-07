using DiscordBot.Domain.Common;
using DiscordBot.Domain.Enums;

namespace DiscordBot.Domain.Entities;

/// <summary>
/// A scheduled event. Its state changes only through its own methods, so its rules hold wherever it is used. The setters are
/// internal: the Domain project (and its tests) can arrange any state, everything else has to go through the methods.
/// </summary>
public class Event
{
    // For EF Core, and for tests that need an event in a particular state
    internal Event()
    {
    }

    public ulong EventId { get; internal set; }
    public ulong GuildId { get; internal set; }
    public ulong CreatedBy { get; internal set; }
    public string Title { get; internal set; } = string.Empty;
    public string Description { get; internal set; } = string.Empty;
    public DateTime ScheduledTime { get; internal set; }
    public DateTime CreatedAt { get; internal set; }
    public DateTime? UpdatedAt { get; internal set; }
    public DateTime? DeletedAt { get; internal set; }
    public bool IsDeleted { get; internal set; }
    public EventStatus Status { get; internal set; } = EventStatus.Scheduled;
    public EventType Type { get; internal set; } = EventType.Generic;
    public ulong? MessageId { get; internal set; }
    public ulong? ChannelId { get; internal set; }
    /// <summary>How long before the start the bot pings people who have not responded. Null means no reminder.</summary>
    public int? ReminderMinutesBefore { get; internal set; }

    public bool ReminderSent { get; internal set; }
    public int? DurationMinutes { get; internal set; }

    /// <summary>Roles pinged when the announcement is posted (and again for each new occurrence of a recurring event).</summary>
    public List<ulong> MentionRoleIds { get; internal set; } = new();

    public bool IsRecurring { get; internal set; }
    public RecurrencePattern? RecurrencePattern { get; internal set; }
    public ulong? ParentRecurringEventId { get; internal set; }

    public Guild? Guild { get; internal set; }
    public ICollection<EventSignup> Signups { get; internal set; } = new List<EventSignup>();

    public bool IsWow => Type == EventType.WowClassic || Type == EventType.WowForever;

    public DateTime EndsAt => ScheduledTime.AddMinutes(DurationMinutes ?? 0);

    /// <summary>
    /// A new event. The values are expected to have passed <see cref="EventRules.ValidateNew"/>; the title and description are
    /// trimmed here so that what is stored is what was validated.
    /// </summary>
    public static Event Schedule(
        ulong eventId, ulong guildId, ulong createdBy, string title, string description, DateTime scheduledTime, EventType type,
        DateTime now, int? reminderMinutes = null, int? durationMinutes = null,
        IEnumerable<ulong>? mentionRoleIds = null, RecurrencePattern? recurrence = null)
    {
        return new Event
        {
            EventId = eventId,
            GuildId = guildId,
            CreatedBy = createdBy,
            Title = title.Trim(),
            Description = description.Trim(),
            ScheduledTime = scheduledTime,
            CreatedAt = now,
            Type = type,
            Status = EventStatus.Scheduled,
            ReminderMinutesBefore = reminderMinutes,
            DurationMinutes = durationMinutes,
            MentionRoleIds = mentionRoleIds?.Distinct().ToList() ?? new List<ulong>(),
            IsRecurring = recurrence.HasValue,
            RecurrencePattern = recurrence
        };
    }

    public bool CanBeChanged => Status == EventStatus.Scheduled && !IsDeleted;

    public bool HasEnded(DateTime now)
    {
        return now >= EndsAt;
    }

    /// <summary>
    /// Applies the values that are set in <paramref name="update"/>, or changes nothing and says which rule they break.
    /// Everything is checked before anything is changed, so a rejected edit never leaves the event half-updated.
    /// </summary>
    public EventValidationError? ApplyChanges(EventUpdate update, DateTime now)
    {
        if (!CanBeChanged)
        {
            return EventValidationError.NotChangeable;
        }

        string? title = null;
        string? description = null;

        if (update.Title != null)
        {
            var titleError = EventRules.ValidateTitle(update.Title, out var trimmedTitle);
            if (titleError != null)
            {
                return titleError;
            }

            title = trimmedTitle;
        }

        if (update.Description != null)
        {
            var descriptionError = EventRules.ValidateDescription(update.Description, out var trimmedDescription);
            if (descriptionError != null)
            {
                return descriptionError;
            }

            description = trimmedDescription;
        }

        var error = (update.ScheduledTime.HasValue ? EventRules.ValidateScheduledTime(update.ScheduledTime.Value, now) : null)
            // 0 means "remove the reminder", so only a value above 0 is checked against the limits
            ?? (update.ReminderMinutes is > 0 ? EventRules.ValidateReminderMinutes(update.ReminderMinutes) : null)
            // 0 means "remove the duration", so only a value above 0 is checked against the limits
            ?? (update.DurationMinutes is > 0 ? EventRules.ValidateDuration(update.DurationMinutes) : null)
            ?? EventRules.ValidateMentionRoles(update.MentionRoleIds);

        if (error != null)
        {
            return error;
        }

        if (title == null && description == null && !update.ScheduledTime.HasValue && !update.ReminderMinutes.HasValue
            && !update.DurationMinutes.HasValue && update.MentionRoleIds == null)
        {
            return EventValidationError.NothingToChange;
        }

        ChangeDetails(title, description, now);

        if (update.ScheduledTime.HasValue)
        {
            Reschedule(update.ScheduledTime.Value, now);
        }

        if (update.ReminderMinutes.HasValue)
        {
            ChangeReminder(update.ReminderMinutes.Value == 0 ? null : update.ReminderMinutes.Value, now);
        }

        if (update.DurationMinutes.HasValue)
        {
            ChangeDuration(update.DurationMinutes.Value == 0 ? null : update.DurationMinutes.Value, now);
        }

        if (update.MentionRoleIds != null)
        {
            ChangeMentionRoles(update.MentionRoleIds, now);
        }

        return null;
    }

    public void MarkReminderSent(DateTime now)
    {
        ReminderSent = true;
        UpdatedAt = now;
    }

    public void Complete(DateTime now)
    {
        Status = EventStatus.Completed;
        UpdatedAt = now;
    }

    /// <summary>Cancels this event, or says why not. The scheduled time is kept so it is still clear when it was meant to happen.</summary>
    public EventValidationError? Cancel(DateTime now)
    {
        if (!CanBeChanged)
        {
            return EventValidationError.NotChangeable;
        }

        Status = EventStatus.Cancelled;
        UpdatedAt = now;
        return null;
    }

    /// <summary>Cancels this event as part of cancelling a recurring series, so it is no longer treated as recurring.</summary>
    public EventValidationError? CancelAsPartOfSeries(DateTime now)
    {
        var error = Cancel(now);
        if (error == null)
        {
            IsRecurring = false;
        }

        return error;
    }

    /// <summary>Soft-deletes the event (it stays in the database but is hidden everywhere). False when it was already deleted.</summary>
    public bool MarkDeleted(DateTime now)
    {
        if (IsDeleted)
        {
            return false;
        }

        IsDeleted = true;
        DeletedAt = now;
        UpdatedAt = now;
        return true;
    }

    public void AttachAnnouncement(ulong channelId, ulong messageId, DateTime now)
    {
        ChannelId = channelId;
        MessageId = messageId;
        UpdatedAt = now;
    }

    /// <summary>
    /// The next occurrence of a recurring event: the same event at a later time, still part of the same series. It starts with no
    /// signups, no reminder sent and no announcement message.
    /// </summary>
    public Event CreateNextOccurrence(ulong eventId, DateTime scheduledTime, DateTime now)
    {
        return new Event
        {
            EventId = eventId,
            GuildId = GuildId,
            CreatedBy = CreatedBy,
            Title = Title,
            Description = Description,
            ScheduledTime = scheduledTime,
            CreatedAt = now,
            Type = Type,
            Status = EventStatus.Scheduled,
            ChannelId = ChannelId,
            IsRecurring = true,
            RecurrencePattern = RecurrencePattern,
            ParentRecurringEventId = ParentRecurringEventId ?? EventId,
            ReminderMinutesBefore = ReminderMinutesBefore,
            ReminderSent = false,
            DurationMinutes = DurationMinutes,
            MentionRoleIds = MentionRoleIds.ToList()
        };
    }

    /// <summary>Moves the event to a new time. The reminder is re-armed, since the old one no longer applies.</summary>
    internal void Reschedule(DateTime scheduledTime, DateTime now)
    {
        ScheduledTime = scheduledTime;
        ReminderSent = false;
        UpdatedAt = now;
    }

    internal void ChangeReminder(int? minutesBefore, DateTime now)
    {
        ReminderMinutesBefore = minutesBefore;
        ReminderSent = false;
        UpdatedAt = now;
    }

    internal void ChangeDuration(int? minutes, DateTime now)
    {
        DurationMinutes = minutes;
        UpdatedAt = now;
    }

    internal void ChangeMentionRoles(IEnumerable<ulong> roleIds, DateTime now)
    {
        MentionRoleIds = roleIds.Distinct().ToList();
        UpdatedAt = now;
    }

    internal void ChangeDetails(string? title, string? description, DateTime now)
    {
        if (title != null)
        {
            Title = title;
        }

        if (description != null)
        {
            Description = description;
        }

        UpdatedAt = now;
    }
}
