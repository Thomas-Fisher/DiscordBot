using Confluent.Kafka;
using Microsoft.Extensions.Hosting;

namespace DiscordBot.Infrastructure.EventBus;

/// <summary>Sends any events still buffered in the producer when the application shuts down.</summary>
public class KafkaProducerLifetime : IHostedService
{
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

    private readonly IProducer<string, string> _producer;

    public KafkaProducerLifetime(IProducer<string, string> producer)
    {
        _producer = producer;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _producer.Flush(FlushTimeout);
        return Task.CompletedTask;
    }
}
