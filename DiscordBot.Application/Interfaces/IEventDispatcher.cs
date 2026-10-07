using DiscordBot.Domain.Events;

namespace DiscordBot.Application.Interfaces;

/// <summary>
/// Runs every <see cref="IEventHandler{T}"/> registered for an event, each in a fresh DI scope. Used by the broker
/// consumer to deliver each event to its handlers.
/// </summary>
public interface IEventDispatcher
{
    /// <summary>
    /// Runs all handlers. A failing handler does not stop the others; if any failed, an
    /// <see cref="AggregateException"/> is thrown afterwards so the caller can retry or dead-letter.
    /// </summary>
    Task DispatchAsync(IDomainEvent @event);
}
