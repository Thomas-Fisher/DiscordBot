namespace DiscordBot.Infrastructure.EventBus;

public class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";

    public string TopicPrefix { get; set; } = "discordbot";

    /// <summary>Consumers with the same group id share the work; each event goes to one of them.</summary>
    public string ConsumerGroupId { get; set; } = "discordbot-api";

    public int Partitions { get; set; } = 3;

    public short ReplicationFactor { get; set; } = 1;

    /// <summary>
    /// Where a group with no saved position starts reading: <c>Latest</c> (default) or <c>Earliest</c>. These events are
    /// commands (a reaction signs someone up), so replaying history would redo old actions.
    /// </summary>
    public string AutoOffsetReset { get; set; } = "Latest";

    /// <summary>How many times a handler failure is retried before the event is moved to the dead-letter topic.</summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>Delay before the first retry; it doubles for each further attempt.</summary>
    public int RetryBackoffMilliseconds { get; set; } = 500;

    public int ProduceTimeoutSeconds { get; set; } = 30;

    /// <summary>How long Kafka keeps messages per topic. Message text passes through these topics, so this is also a privacy setting (Kafka's default is a week).</summary>
    public int RetentionHours { get; set; } = 168;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(BootstrapServers))
        {
            errors.Add("EventBus:Kafka:BootstrapServers is required when the Kafka event bus is used");
        }

        if (string.IsNullOrWhiteSpace(TopicPrefix) || string.IsNullOrWhiteSpace(ConsumerGroupId))
        {
            errors.Add("EventBus:Kafka:TopicPrefix and ConsumerGroupId are required");
        }

        if (Partitions <= 0 || ReplicationFactor <= 0)
        {
            errors.Add("EventBus:Kafka:Partitions and ReplicationFactor must be greater than 0");
        }

        if (!string.Equals(AutoOffsetReset, "Latest", StringComparison.OrdinalIgnoreCase) && !string.Equals(AutoOffsetReset, "Earliest", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("EventBus:Kafka:AutoOffsetReset must be 'Latest' or 'Earliest'");
        }

        if (RetentionHours <= 0)
        {
            errors.Add("EventBus:Kafka:RetentionHours must be greater than 0");
        }

        if (MaxDeliveryAttempts <= 0 || RetryBackoffMilliseconds < 0 || ProduceTimeoutSeconds <= 0)
        {
            errors.Add("EventBus:Kafka: MaxDeliveryAttempts and ProduceTimeoutSeconds must be greater than 0, and RetryBackoffMilliseconds cannot be negative");
        }

        return errors;
    }
}
