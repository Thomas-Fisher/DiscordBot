using Discord;
using Discord.WebSocket;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Entities;
using Microsoft.Extensions.Logging;
using DiscordBot.Discord.Presentation;

namespace DiscordBot.Discord.Services;

/// <summary>
/// Re-posts the announcement in another channel (Discord cannot move a message) and removes the old one. Signups are in the
/// database, so only the emoji clicked on the old message are lost.
/// </summary>
public class EventMessageMover
{
    private readonly DiscordSocketClient _client;
    private readonly DiscordEventAnnouncer _announcer;
    private readonly ILogger<EventMessageMover> _logger;

    public EventMessageMover(DiscordSocketClient client, DiscordEventAnnouncer announcer, ILogger<EventMessageMover> logger)
    {
        _client = client;
        _announcer = announcer;
        _logger = logger;
    }

    public async Task<MoveResult> MoveAsync(Event evt, ITextChannel target, IEventService events, ISignupService signups, ActivityContext context)
    {
        if (evt.ChannelId == target.Id)
        {
            return MoveResult.Fail($"❌ The event is already in {target.Mention}.");
        }

        // Post first: if that fails nothing has changed, and the old message is only removed once the new one is safe
        IUserMessage newMessage;
        IReadOnlyCollection<EventSignup> current;
        try
        {
            current = await signups.GetSignupsAsync(evt.EventId);
            var embed = EventEmbedBuilder.Build(evt, current, GuildMemberNames.For(_client, evt.GuildId));
            newMessage = await _announcer.PostToChannelAsync(target, evt, embed, pingRoles: false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not post event {EventId} to channel {ChannelId}", evt.EventId, target.Id);
            return MoveResult.Fail($"❌ I couldn't post in {target.Mention}. Check that I can view the channel, send messages and add reactions there.");
        }

        var oldChannelId = evt.ChannelId;
        var oldMessageId = evt.MessageId;

        try
        {
            await events.MoveAnnouncementAsync(evt, target.Id, newMessage.Id, context);
        }
        catch
        {
            // The event still points at the old message, so the copy we just posted must not be left behind
            await TryDeleteAsync(target, newMessage.Id);
            throw;
        }

        if (oldChannelId.HasValue && oldMessageId.HasValue && _client.GetChannel(oldChannelId.Value) is IMessageChannel oldChannel)
        {
            await TryDeleteAsync(oldChannel, oldMessageId.Value);
        }

        await AddHoverLinksAsync(evt, current, newMessage);

        _logger.LogInformation("Event {EventId} moved from channel {OldChannel} to {NewChannel}", evt.EventId, oldChannelId, target.Id);
        return MoveResult.Ok();
    }

    /// <summary>The link to a message is only known once it is posted, so the role counts get their hover text in a second edit.</summary>
    private async Task AddHoverLinksAsync(Event evt, IReadOnlyCollection<EventSignup> signups, IUserMessage message)
    {
        try
        {
            var embed = EventEmbedBuilder.Build(evt, signups, GuildMemberNames.For(_client, evt.GuildId), message.GetJumpUrl());
            await message.ModifyAsync(m => m.Embed = embed);
        }
        catch (Exception ex)
        {
            // The next refresh adds them
            _logger.LogWarning(ex, "Could not add the hover links to event message {MessageId}", message.Id);
        }
    }

    private async Task TryDeleteAsync(IMessageChannel channel, ulong messageId)
    {
        try
        {
            await channel.DeleteMessageAsync(messageId);
        }
        catch (Exception ex)
        {
            // Already deleted, or the bot may not delete messages there: the move itself still succeeded
            _logger.LogWarning(ex, "Could not delete message {MessageId} in channel {ChannelId}", messageId, channel.Id);
        }
    }
}
