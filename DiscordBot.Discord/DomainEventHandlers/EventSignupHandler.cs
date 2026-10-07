using Discord;
using Discord.WebSocket;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Discord.Constants;
using DiscordBot.Discord.Services;
using DiscordBot.Discord.Services.WoW;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using DiscordBot.Domain.Events;
using Microsoft.Extensions.Logging;
using DiscordBot.Application.Errors;

namespace DiscordBot.Discord.DomainEventHandlers;

public class EventSignupHandler : IEventHandler<ReactionAddedEvent>
{
    private readonly IEventService _events;
    private readonly ISignupService _signups;
    private readonly ISignupRepository _signupRepository;
    private readonly IRememberedSpecRepository _rememberedSpecs;
    private readonly ISpecPrompter _prompter;
    private readonly DiscordSocketClient _client;
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly ILogger<EventSignupHandler> _logger;

    public EventSignupHandler(
        IEventService events,
        ISignupService signups,
        ISignupRepository signupRepository,
        IRememberedSpecRepository rememberedSpecs,
        ISpecPrompter prompter,
        DiscordSocketClient client,
        IBackgroundTaskQueue backgroundQueue,
        ILogger<EventSignupHandler> logger)
    {
        _events = events;
        _signups = signups;
        _signupRepository = signupRepository;
        _rememberedSpecs = rememberedSpecs;
        _prompter = prompter;
        _client = client;
        _backgroundQueue = backgroundQueue;
        _logger = logger;
    }

    public async Task Handle(ReactionAddedEvent @event)
    {
        switch (@event.Emoji)
        {
            case SignupEmojis.SignedUp:
                await ChangeStatusAsync(@event, (signups, evt, actor) => signups.SignUpAsync(evt, @event.UserId, null, null, actor));
                break;
            case SignupEmojis.SignedOff:
                await ChangeStatusAsync(@event, (signups, evt, actor) => signups.SignOffAsync(evt, @event.UserId, actor));
                break;
            case SignupEmojis.Unsure:
                await ChangeStatusAsync(@event, (signups, evt, actor) => signups.MarkUnsureAsync(evt, @event.UserId, actor));
                break;
            case SignupEmojis.ResetSpec:
                await HandleResetSpecAsync(@event);
                break;
            default:
                if (WowEmotes.IsWowClassEmote(@event.Emoji))
                {
                    await HandleWowClassReactionAsync(@event);
                }

                break;
        }
    }

    /// <summary>Applies a status reaction through the signup service, then removes the reaction in the background (it acts as a button; the embed shows the result).</summary>
    private async Task ChangeStatusAsync(ReactionAddedEvent @event, Func<ISignupService, Event, ActivityContext, Task<Result<int?>>> change)
    {
        var evt = await _events.FindByMessageAsync(@event.MessageId);
        if (evt == null)
        {
            return;
        }

        var result = await change(_signups, evt, ActivityContext.Discord(@event.UserId));

        if (!result.Success)
        {
            _logger.LogDebug("Reaction {Emoji} by user {UserId} on event {EventId} changed nothing: {Reason}",
                @event.Emoji, @event.UserId, evt.EventId, result.Error?.Code);
        }

        // Whatever the outcome, the click has been dealt with, so the reaction goes (a refused one too: it must not sit there looking accepted)
        _backgroundQueue.Enqueue(nameof(RemoveStatusReactionsAsync), _ => RemoveStatusReactionsAsync(evt, @event.UserId));
    }

    private async Task HandleWowClassReactionAsync(ReactionAddedEvent @event)
    {
        var evt = await _events.FindByMessageAsync(@event.MessageId);
        if (evt == null || !evt.IsWow)
        {
            return; // Not a WoW event
        }

        var className = WowEmotes.GetClassNameFromEmote(@event.Emoji);
        if (string.IsNullOrEmpty(className))
        {
            return;
        }

        // A spec they chose for this class before is used as it is: nothing to ask, and the embed shows what they got
        var remembered = await _rememberedSpecs.FindAsync(evt.GuildId, @event.UserId, evt.Type, className);
        if (remembered != null)
        {
            var result = await _signups.SignUpAsync(evt, @event.UserId, className, remembered, ActivityContext.Discord(@event.UserId));
            if (result.Success || result.Error == SignupErrors.EventNotOpen)
            {
                _backgroundQueue.Enqueue(nameof(ISpecPrompter.RemoveClassReactionAsync),
                    _ => _prompter.RemoveClassReactionAsync(evt, @event.UserId, className));
                return;
            }

            // No longer a spec this class can have (the spec list changed): forget it and ask again
            _logger.LogInformation("Remembered spec {Spec} for {Class} by user {UserId} was refused ({Reason}), asking again",
                remembered, className, @event.UserId, result.Error?.Code);
            await _rememberedSpecs.DeleteAsync(evt.GuildId, @event.UserId, evt.Type, className);
        }

        await _prompter.SendPickerAsync(evt, @event.UserId, className);
    }

    /// <summary>Asks a signed-up member for the class again so they can pick another spec; replaces both the signup's spec and the remembered one. A click from someone with no class is ignored.</summary>
    private async Task HandleResetSpecAsync(ReactionAddedEvent @event)
    {
        var evt = await _events.FindByMessageAsync(@event.MessageId);
        if (evt == null)
        {
            return;
        }

        if (evt.IsWow)
        {
            var signup = await _signupRepository.GetEventSignupAsync(evt.EventId, @event.UserId);
            if (signup is { Status: SignupStatus.SignedUp, WowClass: { Length: > 0 } className })
            {
                await _prompter.SendPickerAsync(evt, @event.UserId, className);
            }
            else
            {
                _logger.LogDebug("User {UserId} reset their spec on event {EventId} without being signed up with a class", @event.UserId, evt.EventId);
            }
        }

        _backgroundQueue.Enqueue(nameof(RemoveStatusReactionsAsync), _ => RemoveStatusReactionsAsync(evt, @event.UserId));
    }

    private async Task RemoveStatusReactionsAsync(Event evt, ulong userId)
    {
        try
        {
            if (_client.GetChannel(evt.ChannelId ?? 0) is not IMessageChannel channel)
            {
                _logger.LogWarning("Channel {ChannelId} not found for removing reaction", evt.ChannelId);
                return;
            }

            var message = await channel.GetMessageAsync(evt.MessageId!.Value);
            if (message == null)
            {
                _logger.LogWarning("Message {MessageId} not found in channel {ChannelId}", evt.MessageId, evt.ChannelId);
                return;
            }

            // Only an emoji that someone other than the bot has used can hold this user's reaction (saves a REST call per emoji)
            foreach (var statusEmoji in SignupEmojis.All)
            {
                var emote = SignupEmojis.ToEmote(statusEmoji);
                if (!message.Reactions.TryGetValue(emote, out var metadata) || metadata.ReactionCount - (metadata.IsMe ? 1 : 0) <= 0)
                {
                    continue;
                }

                try
                {
                    await message.RemoveReactionAsync(emote, userId);
                }
                catch (Exception ex) when (DiscordErrors.IsPermanent(ex))
                {
                    // Already gone (the person removed it themselves), or the bot may not remove it: carry on with the others
                    _logger.LogDebug(ex, "Could not remove {Emoji} from message {MessageId}", statusEmoji, evt.MessageId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing status reactions from message {MessageId}", evt.MessageId);
        }
    }
}
