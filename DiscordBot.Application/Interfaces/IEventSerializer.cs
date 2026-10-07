using DiscordBot.Domain.Events;

namespace DiscordBot.Application.Interfaces;

public interface IEventSerializer
{
    EventEnvelope Serialize(IDomainEvent @event);

    /// <summary>
    /// Rebuilds the event. Throws <see cref="InvalidOperationException"/> for an unknown type name or an unreadable
    /// payload, so the consumer can dead-letter the message instead of silently dropping it.
    /// </summary>
    IDomainEvent Deserialize(EventEnvelope envelope);
}
