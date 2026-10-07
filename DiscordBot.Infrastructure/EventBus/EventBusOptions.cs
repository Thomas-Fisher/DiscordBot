using DiscordBot.Application.Options;

namespace DiscordBot.Infrastructure.EventBus;

public class EventBusOptions : IValidatableOptions
{
    public const string SectionName = "EventBus";

    public KafkaOptions Kafka { get; set; } = new();

    public IReadOnlyList<string> Validate()
    {
        return Kafka.Validate();
    }
}
