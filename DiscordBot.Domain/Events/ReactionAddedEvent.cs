using System.Text.Json.Serialization;

namespace DiscordBot.Domain.Events;

public record ReactionAddedEvent : IDomainEvent
{
    public required ulong MessageId { get; init; }
    public required ulong ChannelId { get; init; }
    public required ulong UserId { get; init; }
    public required string Emoji { get; init; }

    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    // Reactions on one message must be processed in the order they happened (sign up, then sign off...)
    [JsonIgnore]
    public string PartitionKey => MessageId.ToString();
}
