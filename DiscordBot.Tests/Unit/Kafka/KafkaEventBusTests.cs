using System.Text;
using Confluent.Kafka;
using DiscordBot.Domain.Events;
using DiscordBot.Infrastructure.EventBus;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace DiscordBot.Tests.Unit.Kafka;

public class KafkaEventBusTests
{
    private sealed class ReadyProvisioner : KafkaTopicProvisioner
    {
        public ReadyProvisioner(KafkaTopics topics)
            : base(Options.Create(new EventBusOptions()), topics, NullLogger<KafkaTopicProvisioner>.Instance)
        {
        }

        public override Task EnsureAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private readonly EventBusOptions _options = new() { Kafka = new KafkaOptions { TopicPrefix = "test", ProduceTimeoutSeconds = 1 } };
    private readonly KafkaTopics _topics = new(new KafkaOptions { TopicPrefix = "test" });
    private string? _sentTopic;
    private Message<string, string>? _sent;

    private KafkaEventBus Create()
    {
        // The Kafka client is replaced by one that records the message it is asked to send
        var producer = new Mock<IProducer<string, string>>();
        producer.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, string>, CancellationToken>((topic, message, _) => { _sentTopic = topic; _sent = message; })
            .ReturnsAsync(new DeliveryResult<string, string> { Topic = "t", Partition = new Partition(1), Offset = new Offset(5) });

        return new KafkaEventBus(producer.Object, new JsonEventSerializer(), _topics, new ReadyProvisioner(_topics), Options.Create(_options), NullLogger<KafkaEventBus>.Instance);
    }

    private string Header(string name) => Encoding.UTF8.GetString(_sent!.Headers.GetLastBytes(name));

    [Fact]
    public async Task Publish_ReactionEvent_SendsItsJsonToTheReactionTopicKeyedByTheMessage()
    {
        var @event = new ReactionAddedEvent { MessageId = 77, ChannelId = 2, UserId = 3, Emoji = "hunter" };

        await Create().PublishAsync(@event);

        Assert.Equal("test.reaction-added.v1", _sentTopic);
        Assert.Equal("77", _sent!.Key);
        Assert.Contains("\"emoji\":\"hunter\"", _sent.Value);
    }

    [Fact]
    public async Task Publish_MessageEvent_CarriesItsTypeIdAndTimeAsHeadersAndIsKeyedByTheGuild()
    {
        var @event = new MessageReceivedEvent { MessageId = 1, GuildId = 9, ChannelId = 2, AuthorId = 3, Content = "hi", CreatedAt = DateTime.UtcNow };

        await Create().PublishAsync(@event);

        Assert.Equal(("message-received.v1", @event.EventId.ToString()), (Header(KafkaHeaders.EventType), Header(KafkaHeaders.EventId)));
        Assert.Equal(@event.OccurredAt, DateTime.Parse(Header(KafkaHeaders.OccurredAt), null, System.Globalization.DateTimeStyles.RoundtripKind));
        Assert.Equal("9", _sent!.Key);
    }

    private sealed class HangingProvisioner : KafkaTopicProvisioner
    {
        public HangingProvisioner(KafkaTopics topics)
            : base(Options.Create(new EventBusOptions()), topics, NullLogger<KafkaTopicProvisioner>.Instance)
        {
        }

        public override async Task EnsureAsync(CancellationToken cancellationToken) => await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    [Fact]
    public async Task Publish_BrokerNeverAcknowledges_GivesUpWithATimeoutInsteadOfWaitingForEver()
    {
        var producer = new Mock<IProducer<string, string>>();
        producer.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .Returns<string, Message<string, string>, CancellationToken>(async (_, _, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new DeliveryResult<string, string>();
            });
        var bus = new KafkaEventBus(producer.Object, new JsonEventSerializer(), _topics, new ReadyProvisioner(_topics), Options.Create(_options), NullLogger<KafkaEventBus>.Instance);

        var failure = await Record.ExceptionAsync(() => bus.PublishAsync(new ReactionAddedEvent { MessageId = 1, ChannelId = 2, UserId = 3, Emoji = "x" }));

        Assert.IsType<TimeoutException>(failure);
    }

    [Fact]
    public async Task Publish_TopicsCannotBeCreatedBecauseKafkaIsDown_GivesUpWithATimeoutInsteadOfWaitingForEver()
    {
        var bus = new KafkaEventBus(new Mock<IProducer<string, string>>().Object, new JsonEventSerializer(), _topics, new HangingProvisioner(_topics), Options.Create(_options), NullLogger<KafkaEventBus>.Instance);

        var failure = await Record.ExceptionAsync(() => bus.PublishAsync(new ReactionAddedEvent { MessageId = 1, ChannelId = 2, UserId = 3, Emoji = "x" }));

        Assert.IsType<TimeoutException>(failure);
    }
}
