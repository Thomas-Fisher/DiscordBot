namespace DiscordBot.Domain.Common;

/// <summary>
/// Only the values that are set are changed. A <paramref name="DurationMinutes"/> or <paramref name="ReminderMinutes"/> of 0 removes the duration or the reminder, and an empty
/// <paramref name="MentionRoleIds"/> list removes the roles.
/// </summary>
public record EventUpdate(
    string? Title = null,
    string? Description = null,
    DateTime? ScheduledTime = null,
    int? ReminderMinutes = null,
    int? DurationMinutes = null,
    IReadOnlyCollection<ulong>? MentionRoleIds = null);
