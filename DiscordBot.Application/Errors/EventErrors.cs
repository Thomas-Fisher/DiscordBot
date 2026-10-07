using DiscordBot.Domain.Common;
using DiscordBot.Domain.Enums;

namespace DiscordBot.Application.Errors;

/// <summary>Why an event request is refused, worded once so the API and the Discord commands agree and quote the same <c>EventRules</c> limits.</summary>
public static class EventErrors
{
    public static readonly Error NotFound = new("event.not_found", "Event not found", ErrorKind.NotFound);
    public static readonly Error TitleRequired = new("event.title_required", "Title is required.");
    public static readonly Error TitleTooShort = new("event.title_too_short", $"Title must be at least {EventRules.MinTitleLength} characters.");
    public static readonly Error TitleTooLong = new("event.title_too_long", $"Title must be {EventRules.MaxTitleLength} characters or fewer.");
    public static readonly Error DescriptionTooLong = new("event.description_too_long", $"Description must be {EventRules.MaxDescriptionLength} characters or fewer.");
    public static readonly Error DateMustBeInFuture = new("event.date_in_past", "The scheduled time must be in the future.");
    public static readonly Error ReminderOutOfRange = new("event.reminder_out_of_range", $"Reminder must be between {EventRules.MinReminderMinutes} and {EventRules.MaxReminderMinutes} minutes.");
    public static readonly Error DurationOutOfRange = new("event.duration_out_of_range", $"Duration must be a whole number of minutes between {EventRules.MinDurationMinutes} and {EventRules.MaxDurationMinutes}.");
    public static readonly Error TooManyMentionRoles = new("event.too_many_roles", $"At most {EventRules.MaxMentionRoles} roles can be pinged.");
    public static readonly Error NotChangeable = new("event.not_changeable", "This event is cancelled or finished, so it can no longer be changed.");
    public static readonly Error UnknownRole = new("event.unknown_role", "One of the roles to ping is not a role in this server.");
    public static readonly Error UnknownChannel = new("event.unknown_channel", "That is not a text channel in this server.");
    public static readonly Error AnnouncementFailed = new("event.announcement_failed", "I couldn't post the event's announcement in the channel. Check that I can view it, send messages, embed links and add reactions there.");
    public static readonly Error UnknownType = new("event.unknown_type", $"The event type must be one of: {string.Join(", ", Enum.GetNames<EventType>())}.");
    public static readonly Error UnknownRecurrence = new("event.unknown_recurrence", $"The repeat pattern must be one of: {string.Join(", ", Enum.GetNames<RecurrencePattern>())}.");
    public static readonly Error NothingToChange = new("event.nothing_to_change", "No changes were made (no new values provided).");

    public static Error From(EventValidationError error)
    {
        return error switch
        {
            EventValidationError.TitleRequired => TitleRequired,
            EventValidationError.TitleTooShort => TitleTooShort,
            EventValidationError.TitleTooLong => TitleTooLong,
            EventValidationError.DescriptionTooLong => DescriptionTooLong,
            EventValidationError.DateMustBeInFuture => DateMustBeInFuture,
            EventValidationError.ReminderOutOfRange => ReminderOutOfRange,
            EventValidationError.DurationOutOfRange => DurationOutOfRange,
            EventValidationError.TooManyMentionRoles => TooManyMentionRoles,
            EventValidationError.NothingToChange => NothingToChange,
            EventValidationError.NotChangeable => NotChangeable,
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, "No text is written for this error")
        };
    }
}
