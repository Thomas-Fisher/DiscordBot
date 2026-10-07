namespace DiscordBot.Contracts;

/// <summary>
/// Only what is set changes: a blank title, or a time in the same minute as the current one, means "leave as is". An empty
/// <see cref="Description"/> clears it (null leaves it). A <see cref="DurationMinutes"/> of 0 removes the duration (null leaves it),
/// and <see cref="MentionRoleIds"/> replaces the roles that are pinged (null leaves them).
/// </summary>
public sealed record UpdateEventRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public DateTime? ScheduledTime { get; init; }
    public int? DurationMinutes { get; init; }

    /// <summary>Minutes before the start that people who have not responded are pinged. 0 removes the reminder; null leaves it.</summary>
    public int? ReminderMinutes { get; init; }
    public List<ulong>? MentionRoleIds { get; init; }
}
