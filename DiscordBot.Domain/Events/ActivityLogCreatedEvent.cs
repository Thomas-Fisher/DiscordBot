using System.Text.Json.Serialization;

namespace DiscordBot.Domain.Events;

public record ActivityLogCreatedEvent : IDomainEvent
{
    public required string ActionType { get; init; }
    public required string EntityType { get; init; }
    public required ulong EntityId { get; init; }
    public required ulong GuildId { get; init; }
    public required ulong? UserId { get; init; }

    public required string? ChangesJson { get; init; }

    /// <summary>Where the change came from (Discord, Api, Recurring or System). Null on events published before it was recorded.</summary>
    public string? Source { get; init; }

    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    [JsonIgnore]
    public string PartitionKey => GuildId.ToString();
}
