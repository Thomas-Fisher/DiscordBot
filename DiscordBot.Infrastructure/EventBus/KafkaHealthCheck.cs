using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DiscordBot.Infrastructure.EventBus;

/// <summary>
/// Broker reachable, topics present, dead-letter count and consumer state. Unhealthy when the broker cannot be reached;
/// degraded when something needs attention.
/// </summary>
public class KafkaHealthCheck : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    // Each check talks to the broker, and /health can be hit as often as a load balancer likes: answer from the last check for a while
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);

    private readonly KafkaOptions _options;
    private readonly KafkaTopics _topics;
    private readonly KafkaConsumerStatus _consumer;
    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();
    private HealthCheckResult _cached;
    private DateTime _cachedAt;

    public KafkaHealthCheck(IOptions<EventBusOptions> options, KafkaTopics topics, KafkaConsumerStatus consumer, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _options = options.Value.Kafka;
        _topics = topics;
        _consumer = consumer;
    }

    // The Kafka admin and consumer clients only have blocking calls, so they run on a pool thread rather than the request's
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                if (_cachedAt == default || now - _cachedAt > CacheFor)
                {
                    _cached = Check();
                    _cachedAt = now;
                }

                return _cached;
            }
        }, cancellationToken);
    }

    private HealthCheckResult Check()
    {
        var data = new Dictionary<string, object>
        {
            ["provider"] = "Kafka",
            ["bootstrapServers"] = _options.BootstrapServers,
            ["consumerGroup"] = _options.ConsumerGroupId,
            ["consumerState"] = _consumer.State.ToString(),
            ["messagesProcessedSinceStart"] = _consumer.MessagesProcessed,
            ["deadLetteredSinceStart"] = _consumer.DeadLettered
        };

        if (_consumer.LastMessageAt is { } last)
        {
            data["lastMessageAt"] = last;
        }

        if (_consumer.LastError != null)
        {
            data["consumerLastError"] = _consumer.LastError;
        }

        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _options.BootstrapServers }).Build();
            var metadata = admin.GetMetadata(Timeout);

            data["brokers"] = metadata.Brokers.Count;

            var existing = metadata.Topics.ToDictionary(t => t.Topic);
            var missing = _topics.AllTopics.Where(t => !existing.ContainsKey(t)).ToList();
            data["topics"] = _topics.AllTopics.Where(existing.ContainsKey).ToList();
            if (missing.Count > 0)
            {
                data["missingTopics"] = missing;
            }

            if (existing.TryGetValue(_topics.DeadLetter, out var deadLetterTopic))
            {
                data["deadLetterMessagesInTopic"] = CountMessages(deadLetterTopic);
            }

            var problems = new List<string>();
            if (missing.Count > 0)
            {
                problems.Add($"topics not created yet: {string.Join(", ", missing)}");
            }

            if (_consumer.State == KafkaConsumerState.Restarting)
            {
                problems.Add("the consumer is restarting after an error");
            }

            if (_consumer.DeadLettered > 0)
            {
                problems.Add($"{_consumer.DeadLettered} event(s) moved to the dead-letter topic since start-up");
            }

            return problems.Count == 0
                ? HealthCheckResult.Healthy($"Kafka is reachable and the consumer is {_consumer.State.ToString().ToLowerInvariant()}", data)
                : HealthCheckResult.Degraded($"Kafka is reachable but: {string.Join("; ", problems)}", data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Kafka at {_options.BootstrapServers} is not reachable", ex, data);
        }
    }

    private long CountMessages(TopicMetadata topic)
    {
        var config = new ConsumerConfig { BootstrapServers = _options.BootstrapServers, GroupId = _options.ConsumerGroupId + "-health" };
        using var consumer = new ConsumerBuilder<Ignore, Ignore>(config).Build();

        long total = 0;
        foreach (var partition in topic.Partitions)
        {
            var offsets = consumer.QueryWatermarkOffsets(new TopicPartition(topic.Topic, partition.PartitionId), Timeout);
            total += offsets.High.Value - offsets.Low.Value;
        }

        return total;
    }
}
