using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordBot.Infrastructure.Background;

public class DataRetentionService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RetentionOptions _options;
    private readonly ILogger<DataRetentionService> _logger;

    public DataRetentionService(IServiceScopeFactory scopeFactory, IOptions<RetentionOptions> options, ILogger<DataRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Data retention is disabled");
            return;
        }

        try
        {
            // Let the bot finish starting before doing bulk deletes
            await Task.Delay(StartupDelay, stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromHours(Math.Max(_options.IntervalHours, 1)));
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

            if (_options.MessageLogDays > 0)
            {
                await scope.ServiceProvider.GetRequiredService<IMessageLogRepository>().DeleteOldMessagesAsync(_options.MessageLogDays);
            }

            if (_options.ActivityLogDays > 0)
            {
                await scope.ServiceProvider.GetRequiredService<IActivityLogRepository>().DeleteOldActivityLogsAsync(_options.ActivityLogDays);
            }

            if (_options.ProcessedEventDays > 0)
            {
                await scope.ServiceProvider.GetRequiredService<IProcessedEventRepository>().DeleteOlderThanAsync(_options.ProcessedEventDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Data retention run failed");
        }
    }
}
