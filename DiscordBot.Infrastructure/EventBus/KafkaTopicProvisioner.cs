using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordBot.Infrastructure.EventBus;

/// <summary>
/// Creates the topics before use (the broker does not auto-create them), retrying until the broker is reachable so the app can
/// start first.
/// </summary>
public class KafkaTopicProvisioner
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly KafkaOptions _options;
    private readonly KafkaTopics _topics;
    private readonly ILogger<KafkaTopicProvisioner> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _done;

    public KafkaTopicProvisioner(IOptions<EventBusOptions> options, KafkaTopics topics, ILogger<KafkaTopicProvisioner> logger)
    {
        _options = options.Value.Kafka;
        _topics = topics;
        _logger = logger;
    }

    public virtual async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (_done)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            while (!_done)
            {
                try
                {
                    await CreateTopicsAsync();
                    _done = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Kafka at {Servers} is not ready; retrying in {Seconds}s", _options.BootstrapServers, RetryDelay.TotalSeconds);
                    await Task.Delay(RetryDelay, cancellationToken);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CreateTopicsAsync()
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _options.BootstrapServers }).Build();

        var specs = _topics.AllTopics.Select(name => new TopicSpecification
        {
            Name = name,
            NumPartitions = _options.Partitions,
            ReplicationFactor = _options.ReplicationFactor,
            Configs = new Dictionary<string, string> { ["retention.ms"] = RetentionMilliseconds }
        }).ToList();

        try
        {
            await admin.CreateTopicsAsync(specs);
            _logger.LogInformation("Created Kafka topics: {Topics}", string.Join(", ", _topics.AllTopics));
        }
        catch (CreateTopicsException ex)
        {
            // Topics that already exist are fine; anything else is a real failure
            var failures = ex.Results.Where(r => r.Error.Code != ErrorCode.TopicAlreadyExists).ToList();
            if (failures.Count > 0)
            {
                throw;
            }

            _logger.LogInformation("Kafka topics already exist");
        }

        // Topics that were created earlier keep whatever retention they had, so the setting is applied to them too
        await admin.IncrementalAlterConfigsAsync(_topics.AllTopics.ToDictionary(
            name => new ConfigResource { Type = ResourceType.Topic, Name = name },
            _ => new List<ConfigEntry> { new() { Name = "retention.ms", Value = RetentionMilliseconds, IncrementalOperation = AlterConfigOpType.Set } }));
    }

    private string RetentionMilliseconds => TimeSpan.FromHours(_options.RetentionHours).TotalMilliseconds.ToString("0");
}
