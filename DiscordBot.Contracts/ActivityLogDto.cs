namespace DiscordBot.Contracts;

public sealed record ActivityLogDto
{
    public ulong LogId { get; init; }
    public DateTime Timestamp { get; init; }
    public required string ActionType { get; init; }
    public required string EntityType { get; init; }
    public ulong? EntityId { get; init; }
    public ulong? UserId { get; init; }

    /// <summary>What the server shows for that user. Null when there is no user, or they are no longer in the server.</summary>
    public string? UserDisplayName { get; init; }

    public string? Source { get; init; }

    /// <summary>What happened, as a sentence with its icon: "🔊 Wez joined #General". Names are the server's own, and times are in its time zone.</summary>
    public string Summary { get; init; } = "";

    public string? Changes { get; init; }
}
