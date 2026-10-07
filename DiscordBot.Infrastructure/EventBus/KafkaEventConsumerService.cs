using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordBot.Infrastructure.EventBus;

/// <summary>
/// Reads every event topic and runs the handlers, one message at a time per partition. The offset is stored only after handling
/// (or dead-lettering), so a crash redelivers a message instead of losing it.
/// </summary>
public class KafkaEventConsumerService : BackgroundService
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LagInterval = TimeSpan.FromSeconds(5);

    private readonly KafkaOptions _options;
    private readonly KafkaTopics _topics;
    private readonly KafkaTopicProvisioner _provisioner;
    private readonly KafkaMessageProcessor _processor;
    private readonly KafkaConsumerStatus _status;
    private readonly ILogger<KafkaEventConsumerService> _logger;
    private DateTime _lagCheckedAt;

    public KafkaEventConsumerService(IOptions<EventBusOptions> options, KafkaTopics topics, KafkaTopicProvisioner provisioner,
        KafkaMessageProcessor processor, KafkaConsumerStatus status, ILogger<KafkaEventConsumerService> logger)
    {
        _status = status;
        _options = options.Value.Kafka;
        _topics = topics;
        _provisioner = provisioner;
        _processor = processor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Don't hold up application start-up while waiting for the broker
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _provisioner.EnsureAsync(stoppingToken);
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _status.MarkRestarting(ex);
                _logger.LogError(ex, "Kafka consumer stopped unexpectedly; restarting in {Seconds}s", RestartDelay.TotalSeconds);
                await Task.Delay(RestartDelay, stoppingToken);
            }
        }

        _status.MarkStopped();
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            AutoOffsetReset = Enum.TryParse<AutoOffsetReset>(_options.AutoOffsetReset, ignoreCase: true, out var startAt) ? startAt : AutoOffsetReset.Latest,
            // Offsets are committed in the background, but only for messages marked as done with StoreOffset below
            EnableAutoCommit = true,
            EnableAutoOffsetStore = false,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
        };

        var workers = new PartitionWorkerSet();

        // When partitions move to another consumer, stop working on them; whatever was not finished is redelivered to the new owner
        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, error) => _logger.LogWarning("Kafka consumer: {Reason}", error.Reason))
            .SetPartitionsRevokedHandler((_, revoked) => workers.StopAsync(revoked.Select(p => p.TopicPartition)).GetAwaiter().GetResult())
            .SetPartitionsLostHandler((_, lost) => workers.StopAsync(lost.Select(p => p.TopicPartition)).GetAwaiter().GetResult())
            .Build();

        consumer.Subscribe(_topics.EventTopics);
        _status.MarkRunning();
        _logger.LogInformation("Kafka consumer {Group} listening to {Topics}", _options.ConsumerGroupId, string.Join(", ", _topics.EventTopics));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (workers.Failure is { } failure)
                {
                    throw new InvalidOperationException("A partition worker failed", failure);
                }

                // A timeout (rather than waiting for ever) so a failed worker is noticed even when no messages arrive
                var result = consumer.Consume(PollInterval);
                RecordLag(consumer);
                if (result?.Message == null)
                {
                    continue;
                }

                await workers.SubmitAsync(result.TopicPartition, async cancellationToken =>
                {
                    await HandleAsync(result, cancellationToken);
                    consumer.StoreOffset(result);
                    _status.MessageProcessed();
                }, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await workers.StopAllAsync();
            consumer.Close();
        }
    }

    /// <summary>How far this consumer's read position is behind the end of its partitions, summed over them. Refreshed every few seconds.</summary>
    private void RecordLag(IConsumer<string, string> consumer)
    {
        var now = DateTime.UtcNow;
        if (now - _lagCheckedAt < LagInterval)
        {
            return;
        }

        _lagCheckedAt = now;

        try
        {
            long lag = 0;
            foreach (var partition in consumer.Assignment)
            {
                var position = consumer.Position(partition);
                if (position.Value >= 0)
                {
                    lag += Math.Max(0, consumer.GetWatermarkOffsets(partition).High.Value - position.Value);
                }
            }

            _status.RecordLag(lag);
        }
        catch (KafkaException ex)
        {
            _logger.LogDebug(ex, "Could not measure the consumer lag");
        }
    }

    private async Task HandleAsync(ConsumeResult<string, string> result, CancellationToken stoppingToken)
    {
        var message = new InboundMessage(
            result.Topic,
            result.Message.Key,
            result.Message.Value,
            Header(result, KafkaHeaders.EventType),
            Header(result, KafkaHeaders.EventId),
            Header(result, KafkaHeaders.OccurredAt),
            Header(result, KafkaHeaders.TraceParent),
            Header(result, KafkaHeaders.TraceState));

        while (true)
        {
            try
            {
                await _processor.ProcessAsync(message, stoppingToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Not even the dead-letter topic accepted it (Kafka trouble). Keep the message and try again rather than lose it.
                _logger.LogError(ex, "Could not process or dead-letter a message from {Topic}; trying again in {Seconds}s", result.Topic, RestartDelay.TotalSeconds);
                await Task.Delay(RestartDelay, stoppingToken);
            }
        }
    }

    private static string? Header(ConsumeResult<string, string> result, string name)
    {
        return result.Message.Headers.TryGetLastBytes(name, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
    }
}
