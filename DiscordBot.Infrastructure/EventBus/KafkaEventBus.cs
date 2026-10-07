using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Telemetry;
using DiscordBot.Domain.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordBot.Infrastructure.EventBus;

/// <summary>
/// Publishes events to Kafka: the key is the partition key (same key, same order), the value the event JSON, and the type, id,
/// time and trace context are headers. Handlers run in <see cref="KafkaEventConsumerService"/>.
/// </summary>
public class KafkaEventBus : IEventBus
{
    private readonly IProducer<string, string> _producer;
    private readonly IEventSerializer _serializer;
    private readonly KafkaTopics _topics;
    private readonly KafkaTopicProvisioner _provisioner;
    private readonly TimeSpan _publishTimeout;
    private readonly ILogger<KafkaEventBus> _logger;

    public KafkaEventBus(IProducer<string, string> producer, IEventSerializer serializer, KafkaTopics topics,
        KafkaTopicProvisioner provisioner, IOptions<EventBusOptions> options, ILogger<KafkaEventBus> logger)
    {
        _publishTimeout = TimeSpan.FromSeconds(Math.Max(options.Value.Kafka.ProduceTimeoutSeconds, 1));
        _producer = producer;
        _serializer = serializer;
        _topics = topics;
        _provisioner = provisioner;
        _logger = logger;
    }

    public async Task PublishAsync(IDomainEvent @event)
    {
        var envelope = _serializer.Serialize(@event);
        var topic = _topics.For(@event.GetType());

        // Bounded: with the broker down, waiting for it (to create topics, or to acknowledge) must not stall the caller for ever
        using var timeout = new CancellationTokenSource(_publishTimeout);

        using var activity = BotTelemetry.Source.StartActivity($"{topic} publish", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", topic);
        activity?.SetTag("messaging.operation.type", "publish");
        activity?.SetTag("discordbot.event.type", envelope.Type);
        activity?.SetTag("discordbot.event.id", envelope.EventId.ToString());

        var message = new Message<string, string>
        {
            Key = envelope.PartitionKey,
            Value = envelope.Payload,
            Headers = new Headers
            {
                { KafkaHeaders.EventType, Encoding.UTF8.GetBytes(envelope.Type) },
                { KafkaHeaders.EventId, Encoding.UTF8.GetBytes(envelope.EventId.ToString()) },
                { KafkaHeaders.OccurredAt, Encoding.UTF8.GetBytes(envelope.OccurredAt.ToString("O")) }
            }
        };

        if (activity?.Id != null)
        {
            message.Headers.Add(KafkaHeaders.TraceParent, Encoding.UTF8.GetBytes(activity.Id));

            if (!string.IsNullOrEmpty(activity.TraceStateString))
            {
                message.Headers.Add(KafkaHeaders.TraceState, Encoding.UTF8.GetBytes(activity.TraceStateString));
            }
        }

        DeliveryResult<string, string> result;
        try
        {
            await _provisioner.EnsureAsync(timeout.Token);
            result = await _producer.ProduceAsync(topic, message, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "timed out");
            throw new TimeoutException($"Kafka did not accept {envelope.Type} {envelope.EventId} within {_publishTimeout.TotalSeconds:0} seconds");
        }

        _logger.LogDebug("Published {EventType} {EventId} to {Topic} [{Partition}] @{Offset}",
            envelope.Type, envelope.EventId, result.Topic, result.Partition.Value, result.Offset.Value);
    }
}
