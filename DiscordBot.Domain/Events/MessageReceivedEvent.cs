using System.Text.Json.Serialization;

namespace DiscordBot.Domain.Events;

public record MessageReceivedEvent : IDomainEvent
{
    public required ulong MessageId { get; init; }
    public required ulong GuildId { get; init; }
    public required ulong ChannelId { get; init; }
    public required ulong AuthorId { get; init; }
    public required string Content { get; init; }
    public required DateTime CreatedAt { get; init; }

    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    [JsonIgnore]
    public string PartitionKey => GuildId.ToString();
}
