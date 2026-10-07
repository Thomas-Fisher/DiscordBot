namespace DiscordBot.Domain.Entities;

/// <summary>Records that a handler has dealt with an event, so a redelivery can be skipped.</summary>
public class ProcessedEvent
{
    public Guid EventId { get; set; }

    public string Handler { get; set; } = null!;

    public DateTime ProcessedAt { get; set; }
}
