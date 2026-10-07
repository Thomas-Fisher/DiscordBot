using DiscordBot.Application.Errors;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Application.Services;

public class EventCreator : IEventCreator
{
    private readonly IEventService _events;
    private readonly IEventAnnouncer _announcer;
    private readonly ILogger<EventCreator> _logger;

    public EventCreator(IEventService events, IEventAnnouncer announcer, ILogger<EventCreator> logger)
    {
        _events = events;
        _announcer = announcer;
        _logger = logger;
    }

    public async Task<Result<Event>> CreateAndAnnounceAsync(NewEventRequest request, ulong channelId, ActivityContext context)
    {
        // The event is saved first because its id is shown on the announcement
        var created = await _events.CreateAsync(request, context);
        if (!created.Success)
        {
            return created;
        }

        var evt = created.Value!;

        try
        {
            var messageId = await _announcer.PostAsync(evt, channelId);
            await _events.AttachAnnouncementAsync(evt, channelId, messageId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not announce event {EventId} in channel {ChannelId}", evt.EventId, channelId);
            await _events.DeleteAsync(evt, context);

            return Result<Event>.Fail(EventErrors.AnnouncementFailed);
        }

        return created;
    }
}
