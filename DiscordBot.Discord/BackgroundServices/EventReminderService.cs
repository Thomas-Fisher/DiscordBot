using DiscordBot.Application.Errors;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Application.Options;
using DiscordBot.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordBot.Discord.BackgroundServices;

/// <summary>
/// Reminds the members of an event's pinged roles who have not answered it, in the event's channel, at the time the event asked
/// for. An event with no pinged roles has nobody to look for, so its reminder does nothing.
/// </summary>
public class EventReminderService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DiscordConnection _connection;
    private readonly ILogger<EventReminderService> _logger;
    private readonly EventReminderOptions _options;

    public EventReminderService(
        IServiceProvider serviceProvider,
        DiscordConnection connection,
        ILogger<EventReminderService> logger,
        IOptions<EventReminderOptions> options)
    {
        _serviceProvider = serviceProvider;
        _connection = connection;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Event reminder service is disabled");
            return;
        }

        _logger.LogInformation("Event reminder service started, checking every {Seconds} seconds", _options.CheckIntervalSeconds);

        try
        {
            await _connection.WaitUntilReadyAsync(stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(_options.CheckIntervalSeconds, 1)));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SendDueRemindersAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>One pass: claims every due reminder (see <c>IEventScheduler</c>) and pings the people who have not responded to its event.</summary>
    public async Task SendDueRemindersAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<IEventScheduler>();
            var pings = scope.ServiceProvider.GetRequiredService<IEventPingService>();

            foreach (var evt in await scheduler.ClaimDueRemindersAsync())
            {
                await RemindAsync(pings, evt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking event reminders");
        }
    }

    private async Task RemindAsync(IEventPingService pings, Event evt)
    {
        try
        {
            var result = await pings.SendAsync(evt, new PingRequest(PingTarget.NoResponse), ActivityContext.System());

            if (result.Success)
            {
                _logger.LogInformation("Reminded {Count} people who had not responded to event {EventId} ({Title})", result.Value, evt.EventId, evt.Title);
            }
            else if (result.Error == PingErrors.NeedsRoles || result.Error == PingErrors.NoOneToPing)
            {
                // No roles to look through, or everyone has already answered: nothing to remind
                _logger.LogDebug("No reminder needed for event {EventId}: {Reason}", evt.EventId, result.Error!.Code);
            }
            else
            {
                _logger.LogWarning("Could not remind people about event {EventId}: {Reason}", evt.EventId, result.Error!.Code);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending the reminder for event {EventId}", evt.EventId);
        }
    }
}
