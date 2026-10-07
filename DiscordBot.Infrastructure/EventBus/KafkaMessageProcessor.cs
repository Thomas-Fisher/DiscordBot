using System.Diagnostics;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordBot.Infrastructure.EventBus;

/// <summary>
/// Per message: rebuild the event, run its handlers, retry with a growing delay, and dead-letter it after the last attempt (at
/// once if it cannot be read) so it stops blocking the partition.
/// </summary>
public class KafkaMessageProcessor
{
    private readonly IEventSerializer _serializer;
    private readonly IEventDispatcher _dispatcher;
    private readonly IDeadLetterSink _deadLetters;
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaMessageProcessor> _logger;

    public KafkaMessageProcessor(IEventSerializer serializer, IEventDispatcher dispatcher, IDeadLetterSink deadLetters,
        IOptions<EventBusOptions> options, ILogger<KafkaMessageProcessor> logger)
    {
        _serializer = serializer;
        _dispatcher = dispatcher;
        _deadLetters = deadLetters;
        _options = options.Value.Kafka;
        _logger = logger;
    }

    public async Task ProcessAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        // Continues the trace of whoever published the message, so the publish and the handlers show up as one trace
        ActivityContext.TryParse(message.TraceParent, message.TraceState, isRemote: true, out var parent);
        using var activity = BotTelemetry.Source.StartActivity($"{message.Topic} process", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", message.Topic);
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("discordbot.event.type", message.EventType);
        activity?.SetTag("discordbot.event.id", message.EventId);

        EventEnvelope envelope;
        try
        {
            envelope = ToEnvelope(message);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Unreadable message on {Topic}; moving it to the dead-letter topic", message.Topic);
            activity?.SetStatus(ActivityStatusCode.Error, "dead-lettered: " + ex.Message);
            await _deadLetters.SendAsync(message, ex.Message, cancellationToken);
            return;
        }

        Domain.Events.IDomainEvent @event;
        try
        {
            @event = _serializer.Deserialize(envelope);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Could not rebuild {EventType} {EventId}; moving it to the dead-letter topic", envelope.Type, envelope.EventId);
            activity?.SetStatus(ActivityStatusCode.Error, "dead-lettered: " + ex.Message);
            await _deadLetters.SendAsync(message, ex.Message, cancellationToken);
            return;
        }

        var attempts = Math.Max(_options.MaxDeliveryAttempts, 1);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _dispatcher.DispatchAsync(@event);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt >= attempts)
                {
                    _logger.LogError(ex, "{EventType} {EventId} failed {Attempts} times; moving it to the dead-letter topic", envelope.Type, envelope.EventId, attempts);
                    activity?.SetStatus(ActivityStatusCode.Error, "dead-lettered after " + attempts + " attempts");
                    await _deadLetters.SendAsync(message, ex.Message, cancellationToken);
                    return;
                }

                activity?.AddEvent(new ActivityEvent("retry", tags: new ActivityTagsCollection { { "attempt", attempt } }));
                var delay = TimeSpan.FromMilliseconds(_options.RetryBackoffMilliseconds * Math.Pow(2, attempt - 1));
                _logger.LogWarning(ex, "{EventType} {EventId} failed (attempt {Attempt} of {Attempts}); retrying in {Delay}", envelope.Type, envelope.EventId, attempt, attempts, delay);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static EventEnvelope ToEnvelope(InboundMessage message)
    {
        if (string.IsNullOrEmpty(message.EventType))
        {
            throw new InvalidOperationException($"Message has no '{KafkaHeaders.EventType}' header");
        }

        if (!Guid.TryParse(message.EventId, out var eventId))
        {
            throw new InvalidOperationException($"Message has no valid '{KafkaHeaders.EventId}' header");
        }

        if (!DateTime.TryParse(message.OccurredAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var occurredAt))
        {
            throw new InvalidOperationException($"Message has no valid '{KafkaHeaders.OccurredAt}' header");
        }

        return new EventEnvelope(message.EventType, eventId, occurredAt, message.Key ?? "", message.Value);
    }
}
