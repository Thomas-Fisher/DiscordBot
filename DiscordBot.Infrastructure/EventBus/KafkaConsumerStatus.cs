using System.Diagnostics.Metrics;
using DiscordBot.Application.Telemetry;

namespace DiscordBot.Infrastructure.EventBus;

public class KafkaConsumerStatus
{
    private static readonly Counter<long> ProcessedCounter = BotTelemetry.Meter.CreateCounter<long>("discordbot.events.processed", description: "Events read from Kafka and handled");
    private static readonly Counter<long> DeadLetteredCounter = BotTelemetry.Meter.CreateCounter<long>("discordbot.events.dead_lettered", description: "Events moved to the dead-letter topic");

    private long _lag;
    private long _messagesProcessed;
    private long _deadLettered;
    private long _lastMessageTicks;

    public KafkaConsumerStatus()
    {
        BotTelemetry.Meter.CreateObservableGauge("discordbot.kafka.consumer.lag", () => Interlocked.Read(ref _lag), description: "Messages between this consumer's position and the end of its partitions");
    }

    public KafkaConsumerState State { get; private set; } = KafkaConsumerState.Starting;

    public string? LastError { get; private set; }

    public long MessagesProcessed => Interlocked.Read(ref _messagesProcessed);

    public long DeadLettered => Interlocked.Read(ref _deadLettered);

    public DateTime? LastMessageAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastMessageTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public void MarkRunning()
    {
        State = KafkaConsumerState.Running;
        LastError = null;
    }

    public void MarkRestarting(Exception error)
    {
        State = KafkaConsumerState.Restarting;
        LastError = error.Message;
    }

    public void MarkStopped()
    {
        State = KafkaConsumerState.Stopped;
    }

    public void MessageProcessed()
    {
        ProcessedCounter.Add(1);
        Interlocked.Increment(ref _messagesProcessed);
        Interlocked.Exchange(ref _lastMessageTicks, DateTime.UtcNow.Ticks);
    }

    public void MessageDeadLettered()
    {
        DeadLetteredCounter.Add(1);
        Interlocked.Increment(ref _deadLettered);
    }

    public void RecordLag(long lag)
    {
        Interlocked.Exchange(ref _lag, lag);
    }
}
