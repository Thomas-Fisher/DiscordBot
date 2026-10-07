using Discord;
using Discord.WebSocket;
using DiscordBot.Application.Activity;
using DiscordBot.Application.Interfaces;
using DiscordBot.Discord.Presentation;
using DiscordBot.Domain.Events;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord.DomainEventHandlers;

public class ActivityLogNotificationHandler : IEventHandler<ActivityLogCreatedEvent>
{
    private readonly ISettingsService _settings;
    private readonly DiscordSocketClient _client;
    private readonly ILogger<ActivityLogNotificationHandler> _logger;

    public ActivityLogNotificationHandler(ISettingsService settings, DiscordSocketClient client, ILogger<ActivityLogNotificationHandler> logger)
    {
        _settings = settings;
        _client = client;
        _logger = logger;
    }

    public async Task Handle(ActivityLogCreatedEvent @event)
    {
        var echoChannelId = (await _settings.GetEchoSettingsAsync(@event.GuildId)).ActiveChannelId;
        if (!echoChannelId.HasValue)
        {
            return;
        }

        var channel = _client.GetChannel(echoChannelId.Value) as IMessageChannel;
        if (channel == null)
        {
            _logger.LogWarning("Log channel {ChannelId} not found for guild {GuildId}", echoChannelId, @event.GuildId);
            return;
        }

        var message = FormatActivityLog(@event);
        try
        {
            await channel.SendMessageAsync(message, allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex) when (DiscordErrors.IsPermanent(ex))
        {
            _logger.LogWarning(ex, "Cannot post to log channel {ChannelId} of guild {GuildId} (missing permission, or it no longer exists)", echoChannelId, @event.GuildId);
        }
    }

    private static string FormatActivityLog(ActivityLogCreatedEvent @event)
    {
        var record = new ActivityRecord(@event.ActionType, @event.EntityType, @event.EntityId, @event.GuildId, @event.UserId, @event.ChangesJson, @event.Source);
        var description = ActivityDescriber.Describe(record, new DiscordActivityText(@event.GuildId));

        return $"{description} · {DiscordTimestamp.ShortDateTime(@event.OccurredAt)}";
    }
}
