namespace DiscordBot.Infrastructure.EventBus;

/// <summary>
/// Names of the Kafka topics: one per event type (<c>{prefix}.{event-name}</c>, for example
/// <c>discordbot.reaction-added.v1</c>) plus one dead-letter topic for events that could not be processed.
/// </summary>
public class KafkaTopics
{
    private readonly string _prefix;

    public KafkaTopics(KafkaOptions options)
    {
        _prefix = options.TopicPrefix;
    }

    public string DeadLetter => $"{_prefix}.dead-letter.v1";

    public string For(Type eventType)
    {
        return $"{_prefix}.{EventTypeRegistry.GetName(eventType)}";
    }

    public IReadOnlyList<string> EventTopics => EventTypeRegistry.RegisteredTypes.Select(For).OrderBy(t => t).ToList();

    public IReadOnlyList<string> AllTopics => EventTopics.Append(DeadLetter).ToList();
}
