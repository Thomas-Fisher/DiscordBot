using DiscordBot.Domain.Events;

namespace DiscordBot.Application.Interfaces;

public interface IEventHandler<T> where T : IDomainEvent
{
    Task Handle(T @event);
}
