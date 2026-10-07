using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using DiscordBot.Application;
using Discord.WebSocket;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Telemetry;
using DiscordBot.Domain.Events;

namespace DiscordBot.Discord.DiscordEventHandlers;

/// <summary>Translates gateway messages into <c>MessageReceivedEvent</c>s. Content is stored by <c>MessageLoggingEventHandler</c>, so no activity-log row is written per message.</summary>
public class MessageReceivedHandler : IGatewayHandler
{
    private readonly IEventBus _eventBus;
    private readonly IBackgroundTaskQueue _backgroundQueue;

    public MessageReceivedHandler(IEventBus eventBus, [FromKeyedServices(BackgroundQueues.Publish)] IBackgroundTaskQueue backgroundQueue)
    {
        _eventBus = eventBus;
        _backgroundQueue = backgroundQueue;
    }

    public void Attach(DiscordSocketClient client)
    {
        client.MessageReceived += HandleAsync;
    }

    private Task HandleAsync(SocketMessage message)
    {
        if (message.Author.IsBot || message.Channel is not SocketTextChannel textChannel)
        {
            return Task.CompletedTask;
        }

        var @event = new MessageReceivedEvent
        {
            MessageId = message.Id,
            GuildId = textChannel.Guild.Id,
            ChannelId = textChannel.Id,
            AuthorId = message.Author.Id,
            Content = message.Content,
            CreatedAt = message.CreatedAt.UtcDateTime
        };

        // The start of this event's trace: the queued publish, the Kafka hop and the handlers all continue from here
        using var span = BotTelemetry.Source.StartActivity("gateway MessageReceived", ActivityKind.Consumer);

        // Never block the gateway thread on database / Discord work; the queue logs any failure
        _backgroundQueue.Enqueue(nameof(MessageReceivedEvent), _ => _eventBus.PublishAsync(@event));

        return Task.CompletedTask;
    }
}
