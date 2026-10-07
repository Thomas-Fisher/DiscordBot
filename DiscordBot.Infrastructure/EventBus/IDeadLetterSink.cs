namespace DiscordBot.Infrastructure.EventBus;

public interface IDeadLetterSink
{
    Task SendAsync(InboundMessage message, string reason, CancellationToken cancellationToken);
}
