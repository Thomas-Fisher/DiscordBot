namespace DiscordBot.Domain.Events;

/// <summary>
/// A fact that has happened. Events must stay plain, serialisable data (they can travel over a message broker),
/// and consumers must be idempotent because a broker can deliver the same event more than once.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }

    DateTime OccurredAt { get; }

    /// <summary>Events with the same key are delivered in order (the broker partitions on it), e.g. the guild, or the message a reaction was added to.</summary>
    string PartitionKey { get; }
}
