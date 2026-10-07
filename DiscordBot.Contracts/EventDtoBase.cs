namespace DiscordBot.Contracts;

public abstract record EventDtoBase
{
    public ulong EventId { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public DateTime ScheduledTime { get; init; }
    public required string Type { get; init; }
    public required string Status { get; init; }
    public ulong CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
    public bool IsRecurring { get; init; }
    public string? RecurrencePattern { get; init; }
    public int? DurationMinutes { get; init; }
    public List<ulong> MentionRoleIds { get; init; } = new();
}
