namespace DiscordBot.Contracts;

/// <summary>
/// A new event, announced in <see cref="ChannelId"/>. <see cref="Type"/> is Generic, WowClassic or WowForever. <see cref="Recurrence"/>
/// is a repeat pattern (Daily, Weekly, BiWeekly, Monthly, EveryMonday ... EverySunday); leave it out for a one-off event.
/// </summary>
public sealed record CreateEventRequest
{
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public DateTime ScheduledTime { get; init; }
    public string Type { get; init; } = "Generic";
    public ulong ChannelId { get; init; }

    /// <summary>
    /// How long before the start the bot pings the members of the pinged roles who have not responded, in minutes (up to a week).
    /// Leave it out for no reminder.
    /// </summary>
    public int? ReminderMinutes { get; init; }

    public int? DurationMinutes { get; init; }
    public List<ulong>? MentionRoleIds { get; init; }
    public string? Recurrence { get; init; }
}
