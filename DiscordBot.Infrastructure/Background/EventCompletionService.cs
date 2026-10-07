using DiscordBot.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Infrastructure.Background;

/// <summary>Marks one-off events as completed once they have ended, so they stop taking signups.</summary>
public class EventCompletionService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventCompletionService> _logger;

    public EventCompletionService(IServiceScopeFactory scopeFactory, ILogger<EventCompletionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            do
            {
                await RunOnceAsync();
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task RunOnceAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var completed = await scope.ServiceProvider.GetRequiredService<IEventService>().CompleteEndedEventsAsync();

            if (completed > 0)
            {
                _logger.LogInformation("Marked {Count} finished events as completed", completed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Completing finished events failed");
        }
    }
}
