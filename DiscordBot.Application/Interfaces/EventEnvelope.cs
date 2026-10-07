namespace DiscordBot.Application.Interfaces;

public record EventEnvelope(string Type, Guid EventId, DateTime OccurredAt, string PartitionKey, string Payload);
