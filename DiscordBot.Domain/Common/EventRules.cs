namespace DiscordBot.Domain.Common;

/// <summary>
/// The rules an event must satisfy, whichever way it is created or edited (Discord command or API).
/// </summary>
public static class EventRules
{
    public const int MinTitleLength = 3;
    public const int MaxTitleLength = 100;
    public const int MaxDescriptionLength = 200;
    public const int MinReminderMinutes = 1;
    public const int MaxReminderMinutes = 10080;   // a week
    public const int MinDurationMinutes = 1;
    public const int MaxDurationMinutes = 1440;
    public const int MaxMentionRoles = 25;

    /// <summary>The first rule a new event breaks (in the order a person fills the form in), or null when it satisfies them all.</summary>
    public static EventValidationError? ValidateNew(
        string? title, string? description, DateTime scheduledTime, int? reminderMinutes, int? durationMinutes,
        IReadOnlyCollection<ulong>? mentionRoleIds, DateTime now)
    {
        return ValidateTitle(title, out _)
            ?? ValidateDescription(description, out _)
            ?? ValidateScheduledTime(scheduledTime, now)
            ?? ValidateReminderMinutes(reminderMinutes)
            ?? ValidateDuration(durationMinutes)
            ?? ValidateMentionRoles(mentionRoleIds);
    }

    public static EventValidationError? ValidateTitle(string? title, out string trimmed)
    {
        trimmed = title?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return EventValidationError.TitleRequired;
        }

        if (trimmed.Length < MinTitleLength)
        {
            return EventValidationError.TitleTooShort;
        }

        return trimmed.Length > MaxTitleLength ? EventValidationError.TitleTooLong : null;
    }

    public static EventValidationError? ValidateDescription(string? description, out string trimmed)
    {
        trimmed = description?.Trim() ?? string.Empty;
        return trimmed.Length > MaxDescriptionLength ? EventValidationError.DescriptionTooLong : null;
    }

    public static EventValidationError? ValidateScheduledTime(DateTime scheduledTime, DateTime now)
    {
        return scheduledTime <= now ? EventValidationError.DateMustBeInFuture : null;
    }

    public static EventValidationError? ValidateDuration(int? minutes)
    {
        return minutes.HasValue && (minutes < MinDurationMinutes || minutes > MaxDurationMinutes) ? EventValidationError.DurationOutOfRange : null;
    }

    public static EventValidationError? ValidateMentionRoles(IReadOnlyCollection<ulong>? roleIds)
    {
        return roleIds is { Count: > MaxMentionRoles } ? EventValidationError.TooManyMentionRoles : null;
    }

    public static EventValidationError? ValidateReminderMinutes(int? minutes)
    {
        return minutes.HasValue && (minutes < MinReminderMinutes || minutes > MaxReminderMinutes) ? EventValidationError.ReminderOutOfRange : null;
    }
}
