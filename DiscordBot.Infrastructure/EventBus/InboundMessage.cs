namespace DiscordBot.Infrastructure.EventBus;

/// <summary>A message as read from Kafka, without any Kafka types, so the processing rules can be tested on their own.</summary>
public record InboundMessage(string Topic, string? Key, string Value, string? EventType, string? EventId, string? OccurredAt,
    string? TraceParent = null, string? TraceState = null);
