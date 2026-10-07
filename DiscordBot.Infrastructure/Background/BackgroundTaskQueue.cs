using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Infrastructure.Background;

public class BackgroundTaskQueue : BackgroundService, IBackgroundTaskQueue
{
    private const int CapacityLimit = 10_000;
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    private readonly Channel<(string Name, Func<CancellationToken, Task> Work, ActivityContext Parent)> _channel =
        Channel.CreateBounded<(string, Func<CancellationToken, Task>, ActivityContext)>(new BoundedChannelOptions(CapacityLimit)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite
        });

    private readonly ILogger<BackgroundTaskQueue> _logger;

    public BackgroundTaskQueue(string name, ILogger<BackgroundTaskQueue> logger)
    {
        Name = name;
        _logger = logger;

        BotTelemetry.Meter.CreateObservableGauge("discordbot.queue.depth", () => new Measurement<int>(Depth, new KeyValuePair<string, object?>("queue", Name)), description: "Work items waiting in a background queue");
    }

    public string Name { get; }

    public int Capacity => CapacityLimit;

    public int Depth => _channel.Reader.Count;

    public bool Enqueue(string name, Func<CancellationToken, Task> work)
    {
        // The work runs later on another thread: remember which trace it belongs to
        if (_channel.Writer.TryWrite((name, work, Activity.Current?.Context ?? default)))
        {
            return true;
        }

        _logger.LogWarning("Background queue {Queue} is full, dropped work item {WorkName}", Name, name);
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var (name, work, parent) in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                await RunAsync(name, work, parent, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down: fall through to drain whatever is left
        }

        await DrainAsync();
    }

    private async Task DrainAsync()
    {
        using var drainCts = new CancellationTokenSource(DrainTimeout);

        while (!drainCts.IsCancellationRequested && _channel.Reader.TryRead(out var item))
        {
            await RunAsync(item.Name, item.Work, item.Parent, drainCts.Token);
        }
    }

    private async Task RunAsync(string name, Func<CancellationToken, Task> work, ActivityContext parent, CancellationToken cancellationToken)
    {
        using var activity = BotTelemetry.Source.StartActivity(name, ActivityKind.Internal, parent);
        activity?.SetTag("discordbot.queue", Name);

        try
        {
            await work(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Background work item {WorkName} cancelled", name);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Background work item {WorkName} failed", name);
        }
    }
}
