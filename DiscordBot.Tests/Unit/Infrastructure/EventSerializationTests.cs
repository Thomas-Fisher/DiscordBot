using System.Text.Json;
using DiscordBot.Application.Interfaces;
using DiscordBot.Domain.Events;
using DiscordBot.Infrastructure.EventBus;

namespace DiscordBot.Tests.Unit.Infrastructure;

/// <summary>
/// Contract tests for the wire format. If one of these fails, a change to an event would break messages that
/// are already in a topic (or that other consumers are reading), so treat it as a versioning decision, not a test fix.
/// </summary>
public class EventSerializationTests
{
    private readonly JsonEventSerializer _serializer = new();

    private static IEnumerable<IDomainEvent> SampleEvents()
    {
        yield return new MessageReceivedEvent
        {
            MessageId = 1_234_567_890_123_456_789UL,
            GuildId = 987_654_321_098_765_432UL,
            ChannelId = 5,
            AuthorId = 6,
            Content = "héllo \"quoted\" 🎉\nnew line",
            CreatedAt = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc)
        };
        yield return new ReactionAddedEvent { MessageId = 10, ChannelId = 20, UserId = 30, Emoji = "<:mage:1553632767303098528>" };
        yield return new ActivityLogCreatedEvent
        {
            ActionType = "Updated",
            EntityType = "Event",
            EntityId = 7,
            GuildId = 8,
            UserId = null,
            ChangesJson = "{\"title\":\"New\",\"nested\":{\"a\":[1,2,3]}}"
        };
    }

    public static IEnumerable<object[]> Samples() => SampleEvents().Select(e => new object[] { e });

    [Theory]
    [MemberData(nameof(Samples))]
    public void Deserialize_AnEnvelopeSentAsPlainText_RebuildsAnIdenticalEvent(IDomainEvent original)
    {
        // Simulates the broker: the whole envelope becomes text on the wire and is rebuilt from that text alone
        var wireText = JsonSerializer.Serialize(_serializer.Serialize(original));
        var received = JsonSerializer.Deserialize<EventEnvelope>(wireText)!;

        var restored = _serializer.Deserialize(received);

        // Records compare by value, including EventId and OccurredAt
        Assert.Equal(original, restored);
    }

    [Fact]
    public void RegisteredEvents_AgainstTheDomainAndTheSamples_LeaveNoEventWithoutARegistrationOrARoundTripSample()
    {
        var domainEvents = typeof(IDomainEvent).Assembly.GetTypes()
            .Where(t => typeof(IDomainEvent).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .ToList();
        var sampled = SampleEvents().Select(e => e.GetType()).ToHashSet();

        var unregistered = domainEvents.Where(t => !EventTypeRegistry.RegisteredTypes.Contains(t)).ToList();
        var unsampled = EventTypeRegistry.RegisteredTypes.Where(t => !sampled.Contains(t)).ToList();

        Assert.NotEmpty(domainEvents);
        Assert.Empty(unregistered);
        Assert.Empty(unsampled);
    }

    [Fact]
    public void WireNames_OfTheRegisteredEvents_AreTheVersionedNamesAlreadyInUse()
    {
        // These strings are the on-the-wire contract; changing one is a breaking change
        var names = new[] { typeof(MessageReceivedEvent), typeof(ReactionAddedEvent), typeof(ActivityLogCreatedEvent) }.Select(EventTypeRegistry.GetName);

        Assert.Equal(new[] { "message-received.v1", "reaction-added.v1", "activity-log-created.v1" }, names);
    }

    [Fact]
    public void Serialize_Event_ProducesCamelCaseJsonWithoutTheDerivedPartitionKey()
    {
        var reaction = new ReactionAddedEvent { MessageId = 1, ChannelId = 2, UserId = 3, Emoji = "x" };

        var payload = _serializer.Serialize(reaction).Payload;

        Assert.Contains("\"messageId\":1", payload);
        Assert.DoesNotContain("partitionKey", payload);
    }

    [Fact]
    public void Deserialize_UnknownEventType_ThrowsSoTheConsumerCanDeadLetterIt()
    {
        var envelope = new EventEnvelope("does-not-exist.v9", Guid.NewGuid(), DateTime.UtcNow, "k", "{}");

        Assert.Throws<InvalidOperationException>(() => _serializer.Deserialize(envelope));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]                      // required members missing
    [InlineData("null")]
    public void Deserialize_UnreadablePayload_ThrowsSoTheConsumerCanDeadLetterIt(string payload)
    {
        var envelope = new EventEnvelope("reaction-added.v1", Guid.NewGuid(), DateTime.UtcNow, "k", payload);

        Assert.Throws<InvalidOperationException>(() => _serializer.Deserialize(envelope));
    }

    private record UnregisteredEvent : IDomainEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        public string PartitionKey => "x";
    }
}
