using System.Diagnostics;
using DiscordBot.Application.Interfaces;
using DiscordBot.Tests.Support;
using DiscordBot.Domain.Events;
using DiscordBot.Infrastructure.EventBus;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DiscordBot.Tests.Unit.Kafka;

public class KafkaMessageProcessorTests
{
    private sealed class ScriptedDispatcher : IEventDispatcher
    {
        private int _failuresLeft;

        public ScriptedDispatcher(int failures = 0)
        {
            _failuresLeft = failures;
        }

        public List<IDomainEvent> Dispatched { get; } = new();

        public int Attempts { get; private set; }

        public ActivityTraceId? TraceIdSeenByHandlers { get; private set; }

        public Task DispatchAsync(IDomainEvent @event)
        {
            Attempts++;
            TraceIdSeenByHandlers = Activity.Current?.TraceId;
            if (_failuresLeft > 0)
            {
                _failuresLeft--;
                throw new AggregateException(new Exception("handler failed"));
            }

            Dispatched.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDeadLetters : IDeadLetterSink
    {
        public bool Broken { get; init; }

        public List<(InboundMessage Message, string Reason)> Sent { get; } = new();

        public Task SendAsync(InboundMessage message, string reason, CancellationToken cancellationToken)
        {
            if (Broken)
            {
                throw new InvalidOperationException("kafka down");
            }

            Sent.Add((message, reason));
            return Task.CompletedTask;
        }
    }

    private readonly JsonEventSerializer _serializer = new();
    private readonly RecordingDeadLetters _deadLetters = new();

    private KafkaMessageProcessor Create(ScriptedDispatcher dispatcher, int maxAttempts = 3, IDeadLetterSink? deadLetters = null) => new(
        _serializer, dispatcher, deadLetters ?? _deadLetters,
        Options.Create(new EventBusOptions { Kafka = new KafkaOptions { MaxDeliveryAttempts = maxAttempts, RetryBackoffMilliseconds = 1 } }),
        NullLogger<KafkaMessageProcessor>.Instance);

    private InboundMessage ValidMessage(out ReactionAddedEvent original)
    {
        original = new ReactionAddedEvent { MessageId = 5, ChannelId = 6, UserId = 7, Emoji = "mage" };
        var envelope = _serializer.Serialize(original);
        return new InboundMessage("test.reaction-added.v1", envelope.PartitionKey, envelope.Payload, envelope.Type,
            envelope.EventId.ToString(), envelope.OccurredAt.ToString("O"));
    }

    public static IEnumerable<object[]> BrokenMessages()
    {
        yield return new object[] { "no event-type header", (Func<InboundMessage, InboundMessage>)(m => m with { EventType = null }) };
        yield return new object[] { "bad event-id header", (Func<InboundMessage, InboundMessage>)(m => m with { EventId = "not-a-guid" }) };
        yield return new object[] { "unknown event type", (Func<InboundMessage, InboundMessage>)(m => m with { EventType = "something-new.v9" }) };
        yield return new object[] { "payload that is not JSON", (Func<InboundMessage, InboundMessage>)(m => m with { Value = "{ this is not json" }) };
    }

    [Fact]
    public async Task Process_ValidMessage_RunsTheHandlersForTheRebuiltEvent()
    {
        var dispatcher = new ScriptedDispatcher();
        var message = ValidMessage(out var original);

        await Create(dispatcher).ProcessAsync(message, CancellationToken.None);

        var dispatched = Assert.IsType<ReactionAddedEvent>(Assert.Single(dispatcher.Dispatched));
        Assert.Equal(original, dispatched);
        Assert.Empty(_deadLetters.Sent);
    }

    [Theory]
    [MemberData(nameof(BrokenMessages))]
    public async Task Process_MessageThatCanNeverBeRead_GoesStraightToTheDeadLetterTopicWithoutRunningHandlers(string description, Func<InboundMessage, InboundMessage> breakIt)
    {
        var dispatcher = new ScriptedDispatcher();
        var message = breakIt(ValidMessage(out _));

        await Create(dispatcher).ProcessAsync(message, CancellationToken.None);

        Assert.True(_deadLetters.Sent.Count == 1, $"{description} should be dead-lettered");
        Assert.Equal(0, dispatcher.Attempts);
    }

    [Fact]
    public async Task Process_HandlerFailsTwiceThenWorks_IsRetriedUntilItSucceedsAndIsNotDeadLettered()
    {
        var dispatcher = new ScriptedDispatcher(failures: 2);

        await Create(dispatcher, maxAttempts: 5).ProcessAsync(ValidMessage(out _), CancellationToken.None);

        Assert.Equal(3, dispatcher.Attempts);
        Assert.Single(dispatcher.Dispatched);
        Assert.Empty(_deadLetters.Sent);
    }

    [Fact]
    public async Task Process_DeadLetterTopicIsAlsoDown_TheFailureIsNotSwallowedSoTheMessageIsNotLost()
    {
        var processor = Create(new ScriptedDispatcher(), deadLetters: new RecordingDeadLetters { Broken = true });
        var unreadable = ValidMessage(out _) with { EventType = null };

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(unreadable, CancellationToken.None));
    }

    [Fact]
    public async Task ProcessAsync_MessageWithoutTraceContext_StartsATraceOfItsOwn()
    {
        using var spans = new SpanRecorder();
        var dispatcher = new ScriptedDispatcher();

        await Create(dispatcher).ProcessAsync(ValidMessage(out _), CancellationToken.None);

        var span = Assert.Single(spans.Spans, s => s.Kind == ActivityKind.Consumer);
        Assert.Equal(default, span.ParentSpanId);
        Assert.Equal(span.TraceId, dispatcher.TraceIdSeenByHandlers);
    }

    [Fact]
    public async Task ProcessAsync_HandlerFailsEveryAttempt_MarksTheSpanAsFailedAndRecordsTheRetries()
    {
        using var spans = new SpanRecorder();

        await Create(new ScriptedDispatcher(failures: 99), maxAttempts: 3).ProcessAsync(ValidMessage(out _), CancellationToken.None);

        var span = Assert.Single(spans.Spans, s => s.Kind == ActivityKind.Consumer);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(2, span.Events.Count(e => e.Name == "retry"));
    }
}
