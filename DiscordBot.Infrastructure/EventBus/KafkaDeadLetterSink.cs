using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Infrastructure.EventBus;

/// <summary>
/// Copies a message that could not be processed to the dead-letter topic, with the topic it came from and the reason,
/// so it can be inspected (in Kafka UI) and replayed by hand once the cause is fixed.
/// </summary>
public class KafkaDeadLetterSink : IDeadLetterSink
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaTopics _topics;
    private readonly KafkaConsumerStatus _status;
    private readonly ILogger<KafkaDeadLetterSink> _logger;

    public KafkaDeadLetterSink(IProducer<string, string> producer, KafkaTopics topics, KafkaConsumerStatus status, ILogger<KafkaDeadLetterSink> logger)
    {
        _status = status;
        _producer = producer;
        _topics = topics;
        _logger = logger;
    }

    public async Task SendAsync(InboundMessage message, string reason, CancellationToken cancellationToken)
    {
        var headers = new Headers
        {
            { KafkaHeaders.OriginalTopic, Encoding.UTF8.GetBytes(message.Topic) },
            { KafkaHeaders.Error, Encoding.UTF8.GetBytes(reason.Length > 500 ? reason[..500] : reason) }
        };

        AddIfPresent(headers, KafkaHeaders.EventType, message.EventType);
        AddIfPresent(headers, KafkaHeaders.EventId, message.EventId);
        AddIfPresent(headers, KafkaHeaders.OccurredAt, message.OccurredAt);
        AddIfPresent(headers, KafkaHeaders.TraceParent, message.TraceParent);
        AddIfPresent(headers, KafkaHeaders.TraceState, message.TraceState);

        await _producer.ProduceAsync(_topics.DeadLetter, new Message<string, string>
        {
            Key = message.Key ?? "",
            Value = message.Value,
            Headers = headers
        }, cancellationToken);

        _status.MessageDeadLettered();
        _logger.LogWarning("Moved a message from {Topic} to {DeadLetterTopic}: {Reason}", message.Topic, _topics.DeadLetter, reason);
    }

    private static void AddIfPresent(Headers headers, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            headers.Add(name, Encoding.UTF8.GetBytes(value));
        }
    }
}
