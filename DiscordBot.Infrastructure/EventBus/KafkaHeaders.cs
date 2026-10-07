namespace DiscordBot.Infrastructure.EventBus;

public static class KafkaHeaders
{
    public const string EventType = "event-type";
    public const string EventId = "event-id";
    public const string OccurredAt = "occurred-at";

    /// <summary>W3C trace context, so one trace follows an event from the gateway through Kafka to its handlers.</summary>
    public const string TraceParent = "traceparent";
    public const string TraceState = "tracestate";
    public const string OriginalTopic = "original-topic";
    public const string Error = "error";
}
