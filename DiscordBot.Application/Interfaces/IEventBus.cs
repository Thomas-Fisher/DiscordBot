using DiscordBot.Domain.Events;

namespace DiscordBot.Application.Interfaces;

public interface IEventBus
{
    Task PublishAsync(IDomainEvent @event);
}
