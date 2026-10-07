using Discord;
using Discord.WebSocket;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Options;
using DiscordBot.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DiscordBot.Discord.Services;

namespace DiscordBot.Discord.BackgroundServices;

/// <summary>Rolls recurring events forward (the rules are <see cref="IEventScheduler"/>'s) and announces each new occurrence in Discord.</summary>
public class RecurringEventService : BackgroundService
{
    private readonly HashSet<ulong> _failedAnnouncements = new();

    private readonly IServiceProvider _serviceProvider;
    private readonly DiscordSocketClient _client;
    private readonly DiscordConnection _connection;
    private readonly DiscordEventAnnouncer _announcer;
    private readonly RecurrenceOptions _options;
    private readonly ILogger<RecurringEventService> _logger;

    public RecurringEventService(IServiceProvider serviceProvider, DiscordSocketClient client, DiscordConnection connection, DiscordEventAnnouncer announcer,
        IOptions<RecurrenceOptions> options, ILogger<RecurringEventService> logger)
    {
        _serviceProvider = serviceProvider;
        _client = client;
        _connection = connection;
        _announcer = announcer;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _connection.WaitUntilReadyAsync(stoppingToken);

            _logger.LogInformation("RecurringEventService started, checking every {Seconds} seconds", _options.CheckIntervalSeconds);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.CheckIntervalSeconds));
            do
            {
                try
                {
                    await RollForwardDueEventsAsync();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error processing recurring events");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// One pass: every recurring event whose time has come is replaced by its next occurrence, and every event that has no
    /// announcement yet is announced. The two are separate on purpose: the new occurrence is saved before it is posted, so a failed
    /// or interrupted post is simply tried again on the next pass instead of leaving an event nobody can sign up to.
    /// </summary>
    public async Task RollForwardDueEventsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IEventScheduler>();
        var events = scope.ServiceProvider.GetRequiredService<IEventService>();

        await scheduler.RollForwardRecurringEventsAsync();

        foreach (var unannounced in await scheduler.GetUnannouncedEventsAsync())
        {
            await AnnounceAsync(events, unannounced);
        }
    }

    private async Task AnnounceAsync(IEventService events, Event evt)
    {
        try
        {
            if (evt.ChannelId is not { } channelId || _client.GetChannel(channelId) is not ITextChannel channel)
            {
                LogNotAnnounced(evt, "its channel is not available");
                return;
            }

            var message = await _announcer.PostToChannelAsync(channel, evt);
            await events.AttachAnnouncementAsync(evt, channel.Id, message.Id);
            _failedAnnouncements.Remove(evt.EventId);

            _logger.LogInformation("Announced event {EventId} in channel {ChannelId}", evt.EventId, channel.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNotAnnounced(evt, ex.Message);
        }
    }

    // Reported once per event, not once a minute for as long as the problem lasts
    private void LogNotAnnounced(Event evt, string reason)
    {
        if (_failedAnnouncements.Add(evt.EventId))
        {
            _logger.LogWarning("Event {EventId} has no announcement in Discord yet ({Reason}); it will be tried again", evt.EventId, reason);
        }
    }
}
