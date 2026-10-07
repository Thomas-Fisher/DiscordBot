namespace DiscordBot.Infrastructure.EventBus;

public enum KafkaConsumerState
{
    Starting,
    Running,
    Restarting,
    Stopped
}
