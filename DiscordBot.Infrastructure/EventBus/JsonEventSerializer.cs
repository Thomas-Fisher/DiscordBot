using System.Text.Json;
using DiscordBot.Application.Interfaces;
using DiscordBot.Domain.Events;

namespace DiscordBot.Infrastructure.EventBus;

public class JsonEventSerializer : IEventSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public EventEnvelope Serialize(IDomainEvent @event)
    {
        var eventType = @event.GetType();

        return new EventEnvelope(
            Type: EventTypeRegistry.GetName(eventType),
            EventId: @event.EventId,
            OccurredAt: @event.OccurredAt,
            PartitionKey: @event.PartitionKey,
            Payload: JsonSerializer.Serialize(@event, eventType, Options));
    }

    public IDomainEvent Deserialize(EventEnvelope envelope)
    {
        if (!EventTypeRegistry.TryResolve(envelope.Type, out var eventType))
        {
            throw new InvalidOperationException($"Unknown event type '{envelope.Type}'");
        }

        try
        {
            return (IDomainEvent?)JsonSerializer.Deserialize(envelope.Payload, eventType, Options)
                ?? throw new InvalidOperationException($"Event payload for '{envelope.Type}' was empty");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Event payload for '{envelope.Type}' could not be read", ex);
        }
    }
}
