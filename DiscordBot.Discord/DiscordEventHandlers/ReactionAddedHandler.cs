using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using DiscordBot.Application;
using Discord;
using Discord.WebSocket;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Telemetry;
using DiscordBot.Domain.Events;

namespace DiscordBot.Discord.DiscordEventHandlers;

/// <summary>
/// Turns reactions people add into <see cref="ReactionAddedEvent"/>s for the event handlers (signup and so on).
/// Recording the reaction in the activity log is the auditor's job.
/// </summary>
public class ReactionAddedHandler : IGatewayHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundTaskQueue _backgroundQueue;

    public ReactionAddedHandler(IServiceScopeFactory scopeFactory, [FromKeyedServices(BackgroundQueues.Publish)] IBackgroundTaskQueue backgroundQueue)
    {
        _scopeFactory = scopeFactory;
        _backgroundQueue = backgroundQueue;
    }

    public void Attach(DiscordSocketClient client)
    {
        client.ReactionAdded += HandleAsync;
    }

    private Task HandleAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        if (reaction.User.IsSpecified && reaction.User.Value.IsBot)
        {
            return Task.CompletedTask;
        }

        var emoji = reaction.Emote.ToString();
        if (string.IsNullOrEmpty(emoji))
        {
            return Task.CompletedTask;
        }

        var @event = new ReactionAddedEvent
        {
            MessageId = message.Id,
            ChannelId = channel.Id,
            UserId = reaction.UserId,
            Emoji = emoji
        };

        // The start of this event's trace: the queued publish, the Kafka hop and the handlers all continue from here
        using var span = BotTelemetry.Source.StartActivity("gateway ReactionAdded", ActivityKind.Consumer);

        // Stored off the gateway thread, in order (reactions on one message must be applied in the order they happened). It goes through
        // the outbox, so a broker that is down delays the signup instead of losing it
        _backgroundQueue.Enqueue(nameof(ReactionAddedEvent), async cancellationToken =>
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IOutbox>().AddAsync(@event, cancellationToken);
        });

        return Task.CompletedTask;
    }
}
