using Discord;
using Discord.WebSocket;
using DiscordBot.Application.Interfaces;
using DiscordBot.Discord.Services.WoW;
using DiscordBot.Domain.Events;
using Microsoft.Extensions.Logging;
using DiscordBot.Discord.Constants;

namespace DiscordBot.Discord.DomainEventHandlers;

// Removing a reaction again changes nothing, so a repeat delivery is harmless
[NaturallyIdempotent]
public class UnauthorizedReactionHandler : IEventHandler<ReactionAddedEvent>
{
    private readonly IEventService _events;
    private readonly DiscordSocketClient _client;
    private readonly ILogger<UnauthorizedReactionHandler> _logger;

    public UnauthorizedReactionHandler(IEventService events, DiscordSocketClient client, ILogger<UnauthorizedReactionHandler> logger)
    {
        _events = events;
        _client = client;
        _logger = logger;
    }

    public async Task Handle(ReactionAddedEvent @event)
    {
        // The signup status reactions belong to EventSignupHandler
        if (SignupEmojis.All.Contains(@event.Emoji))
        {
            return;
        }

        if (WowEmotes.IsWowClassEmote(@event.Emoji))
        {
            return;
        }

        var evt = await _events.FindByMessageAsync(@event.MessageId);
        if (evt == null)
        {
            return;
        }

        var channel = _client.GetChannel(evt.ChannelId ?? 0) as IMessageChannel;
        if (channel == null)
        {
            return;
        }

        var message = await channel.GetMessageAsync(@event.MessageId);
        if (message == null)
        {
            return;
        }

        try
        {
            await message.RemoveReactionAsync(SignupEmojis.ToEmote(@event.Emoji), @event.UserId);
        }
        catch (Exception ex) when (DiscordErrors.IsPermanent(ex))
        {
            _logger.LogWarning(ex, "Cannot remove reaction {Emoji} from event message {MessageId} (missing permission, or it no longer exists)", @event.Emoji, @event.MessageId);
            return;
        }

        _logger.LogInformation("Removed unauthorized reaction {Emoji} from event message {MessageId} by {UserId}",
            @event.Emoji, @event.MessageId, @event.UserId);
    }
}
