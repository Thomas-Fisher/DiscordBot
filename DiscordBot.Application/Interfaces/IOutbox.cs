using DiscordBot.Domain.Events;

namespace DiscordBot.Application.Interfaces;

/// <summary>Where events that must not be lost go: written to the database, then published by a relay. At-least-once, in write order.</summary>
public interface IOutbox
{
    /// <summary>
    /// Adds the event to the current unit of work without saving it. It is stored by the next save, in the same transaction as
    /// whatever else that save holds: the change and the announcement of it happen together or not at all.
    /// </summary>
    void Stage(IDomainEvent @event);

    /// <summary>Forgets the events that were staged but not saved, for when the save they were waiting for failed.</summary>
    void DiscardStaged();

    /// <summary>Stores the event now.</summary>
    Task AddAsync(IDomainEvent @event, CancellationToken cancellationToken = default);
}
