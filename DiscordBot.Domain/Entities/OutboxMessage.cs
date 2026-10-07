namespace DiscordBot.Domain.Entities;

/// <summary>
/// An event waiting to be published. It is written in the same transaction as the change it describes, so the two happen together
/// or not at all, and a relay publishes it afterwards. See <c>IOutbox</c>.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }

    /// <summary>The event's wire type name (see <c>EventTypeRegistry</c>).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Events with the same key are published in the order they were written.</summary>
    public string PartitionKey { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTime OccurredAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Null until the relay has published it.</summary>
    public DateTime? PublishedAt { get; set; }
}
