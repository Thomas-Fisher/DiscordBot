using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DiscordBot.Application.Interfaces;
using DiscordBot.Domain.Events;
using DiscordBot.Infrastructure.EventBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DiscordBot.Tests.Support;
using Microsoft.Extensions.Options;

namespace DiscordBot.Tests.Integration.Kafka;

public class KafkaEndToEndTests
{
    private class Received
    {
        private readonly List<ReactionAddedEvent> _events = new();
        private readonly List<ActivityTraceId?> _traces = new();
        private readonly object _lock = new();

        public ActivityTraceId? TraceOf(int index)
        {
            lock (_lock)
            {
                return _traces[index];
            }
        }

        public void Add(ReactionAddedEvent e)
        {
            lock (_lock)
            {
                _events.Add(e);
                _traces.Add(Activity.Current?.TraceId);
            }
        }

        public List<ReactionAddedEvent> Snapshot()
        {
            lock (_lock)
            {
                return _events.ToList();
            }
        }
    }

    [NaturallyIdempotent]
    private class RecordingHandler : IEventHandler<ReactionAddedEvent>
    {
        private readonly Received _received;

        public RecordingHandler(Received received)
        {
            _received = received;
        }

        public Task Handle(ReactionAddedEvent @event)
        {
            _received.Add(@event);
            return Task.CompletedTask;
        }
    }

    [NaturallyIdempotent]
    private class AlwaysFailingHandler : IEventHandler<ActivityLogCreatedEvent>
    {
        public Task Handle(ActivityLogCreatedEvent @event) => throw new InvalidOperationException("this handler never works");
    }

    private static readonly TimeSpan KafkaWait = TimeSpan.FromSeconds(45);

    private sealed class Harness : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required KafkaEventConsumerService Consumer { get; init; }
        public required KafkaTopics Topics { get; init; }
        public required Received Received { get; init; }
        public required string Bootstrap { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Consumer.StopAsync(CancellationToken.None);
            Provider.GetRequiredService<IProducer<string, string>>().Flush(TimeSpan.FromSeconds(5));

            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = Bootstrap }).Build();
            try
            {
                await admin.DeleteTopicsAsync(Topics.AllTopics);
            }
            catch (DeleteTopicsException)
            {
                // Best effort clean-up
            }

            await Provider.DisposeAsync();
        }
    }

    private static async Task<Harness> StartAsync()
    {
        var bootstrap = await TestContainers.KafkaBootstrapAsync();
        var id = Guid.NewGuid().ToString("N")[..8];
        var options = new EventBusOptions
        {
            Kafka = new KafkaOptions
            {
                BootstrapServers = bootstrap,
                TopicPrefix = "it" + id,
                ConsumerGroupId = "it-group-" + id,
                AutoOffsetReset = "Earliest",   // the test publishes before the consumer has joined the group
                Partitions = 2,
                MaxDeliveryAttempts = 2,
                RetryBackoffMilliseconds = 10
            }
        };

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(Options.Create(options));
        services.AddSingleton(new KafkaTopics(options.Kafka));
        services.AddSingleton<KafkaTopicProvisioner>();
        services.AddSingleton<KafkaConsumerStatus>();
        services.AddSingleton<IEventSerializer, JsonEventSerializer>();
        services.AddSingleton<IEventDispatcher, EventDispatcher>();
        services.AddSingleton<IProducer<string, string>>(_ => new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrap,
            Acks = Acks.All,
            EnableIdempotence = true
        }).Build());
        services.AddSingleton<IEventBus, KafkaEventBus>();
        services.AddSingleton<IDeadLetterSink, KafkaDeadLetterSink>();
        services.AddSingleton<KafkaMessageProcessor>();
        services.AddSingleton<KafkaEventConsumerService>();

        var received = new Received();
        services.AddSingleton(received);
        services.AddScoped<IEventHandler<ReactionAddedEvent>, RecordingHandler>();
        services.AddScoped<IEventHandler<ActivityLogCreatedEvent>, AlwaysFailingHandler>();

        var provider = services.BuildServiceProvider();
        var consumer = provider.GetRequiredService<KafkaEventConsumerService>();
        await consumer.StartAsync(CancellationToken.None);

        return new Harness { Provider = provider, Consumer = consumer, Topics = provider.GetRequiredService<KafkaTopics>(), Received = received, Bootstrap = bootstrap };
    }

    private static async Task<ConsumeResult<string, string>> ReadDeadLetterAsync(Harness harness)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = harness.Bootstrap,
            GroupId = "it-dlq-" + Guid.NewGuid().ToString("N"),
            AutoOffsetReset = AutoOffsetReset.Earliest
        };
        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(harness.Topics.DeadLetter);

        return await Eventually.ProducesAsync(() => consumer.Consume(TimeSpan.FromMilliseconds(500)), "a dead-letter message", KafkaWait);
    }

    [Fact]
    public async Task Publish_InsideATrace_TheHandlerRunsInTheSameTraceAfterCrossingKafka()
    {
        using var spans = new SpanRecorder();
        await using var harness = await StartAsync();
        var bus = harness.Provider.GetRequiredService<IEventBus>();
        var @event = new ReactionAddedEvent { MessageId = 4343, ChannelId = 1, UserId = 2, Emoji = "priest" };
        using var gatewayEvent = DiscordBot.Application.Telemetry.BotTelemetry.Source.StartActivity("gateway event")!;

        await bus.PublishAsync(@event);
        await Eventually.ProducesAsync(() => harness.Received.Snapshot().FirstOrDefault(e => e.EventId == @event.EventId), "the handler to receive the event", KafkaWait);

        Assert.Equal(gatewayEvent.TraceId, harness.Received.TraceOf(0));
        Assert.Contains(spans.Spans, s => s.Kind == ActivityKind.Producer && s.TraceId == gatewayEvent.TraceId);
        Assert.Contains(spans.Spans, s => s.Kind == ActivityKind.Consumer && s.TraceId == gatewayEvent.TraceId);
    }

    [Fact]
    public async Task Start_TopicsAreCreatedWithTheConfiguredRetention()
    {
        await using var harness = await StartAsync();
        await harness.Provider.GetRequiredService<KafkaTopicProvisioner>().EnsureAsync(CancellationToken.None);
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = harness.Bootstrap }).Build();

        // Seven days, in milliseconds, is what the options default to
        var configs = await admin.DescribeConfigsAsync(new[] { new ConfigResource { Type = ResourceType.Topic, Name = harness.Topics.DeadLetter } });
        Assert.Equal("604800000", configs.Single().Entries["retention.ms"].Value);
    }

    [Fact]
    public async Task Publish_TwentyEventsWithTheSameKey_AreHandledInTheOrderTheyWerePublished()
    {
        await using var harness = await StartAsync();
        var bus = harness.Provider.GetRequiredService<IEventBus>();
        var sent = Enumerable.Range(0, 20).Select(i => new ReactionAddedEvent { MessageId = 99, ChannelId = 1, UserId = (ulong)i, Emoji = "e" + i }).ToList();

        foreach (var e in sent)
        {
            await bus.PublishAsync(e);
        }

        var all = await Eventually.ProducesAsync(() => harness.Received.Snapshot() is { Count: 20 } list ? list : null, "all 20 events", KafkaWait);
        Assert.Equal(sent.Select(e => e.EventId), all.Select(e => e.EventId));
    }

    [Fact]
    public async Task Consume_UnreadableMessageOnAnEventTopic_MovesToTheDeadLetterTopicAndDoesNotBlockTheEventBehindIt()
    {
        // Garbage on an event topic, then a good event behind it on the same partition
        await using var harness = await StartAsync();
        var bus = harness.Provider.GetRequiredService<IEventBus>();
        var producer = harness.Provider.GetRequiredService<IProducer<string, string>>();
        await harness.Provider.GetRequiredService<KafkaTopicProvisioner>().EnsureAsync(CancellationToken.None);
        await producer.ProduceAsync(harness.Topics.For(typeof(ReactionAddedEvent)), new Message<string, string>
        {
            Key = "7",
            Value = "{ definitely not an event",
            Headers = new Headers { { KafkaHeaders.EventType, Encoding.UTF8.GetBytes("reaction-added.v1") } }
        });
        var good = new ReactionAddedEvent { MessageId = 7, ChannelId = 1, UserId = 2, Emoji = "after-garbage" };

        await bus.PublishAsync(good);

        await Eventually.ProducesAsync(() => harness.Received.Snapshot().FirstOrDefault(e => e.EventId == good.EventId), "the good event behind the bad one", KafkaWait);
        var dead = await ReadDeadLetterAsync(harness);
        Assert.Equal(harness.Topics.For(typeof(ReactionAddedEvent)), Encoding.UTF8.GetString(dead.Message.Headers.GetLastBytes(KafkaHeaders.OriginalTopic)));
        Assert.Contains("definitely not an event", dead.Message.Value);
    }

    [Fact]
    public async Task Consume_EventWhoseHandlerAlwaysFails_IsRetriedThenMovedToTheDeadLetterTopicWithTheReason()
    {
        await using var harness = await StartAsync();
        var bus = harness.Provider.GetRequiredService<IEventBus>();
        var @event = new ActivityLogCreatedEvent { ActionType = "X", EntityType = "Y", EntityId = 1, GuildId = 5, UserId = null, ChangesJson = null };

        await bus.PublishAsync(@event);

        var dead = await ReadDeadLetterAsync(harness);
        Assert.Equal(@event.EventId.ToString(), Encoding.UTF8.GetString(dead.Message.Headers.GetLastBytes(KafkaHeaders.EventId)));
        Assert.Contains("never works", Encoding.UTF8.GetString(dead.Message.Headers.GetLastBytes(KafkaHeaders.Error)));
    }
}
