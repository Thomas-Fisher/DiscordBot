using Discord;
using Discord.WebSocket;
using DiscordBot.Application;
using DiscordBot.Application.Interfaces;
using DiscordBot.Discord.Presentation;
using DiscordBot.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord.Services;

public class DiscordEventAnnouncer : IEventAnnouncer
{
    private readonly DiscordSocketClient _client;
    private readonly IBackgroundTaskQueue _slowQueue;
    private readonly ILogger<DiscordEventAnnouncer> _logger;

    public DiscordEventAnnouncer(DiscordSocketClient client, [FromKeyedServices(BackgroundQueues.Maintenance)] IBackgroundTaskQueue slowQueue, ILogger<DiscordEventAnnouncer> logger)
    {
        _client = client;
        _slowQueue = slowQueue;
        _logger = logger;
    }

    public async Task<ulong> PostAsync(Event evt, ulong channelId)
    {
        if (_client.GetChannel(channelId) is not IMessageChannel channel)
        {
            throw new InvalidOperationException($"Channel {channelId} is not available to the bot");
        }

        return (await PostToChannelAsync(channel, evt)).Id;
    }

    /// <summary>
    /// Sends the announcement and returns as soon as the message exists. Its signup reactions are added in the background, so the
    /// caller can link the event to the message at once: reactions clicked before that link exists are ignored.
    /// </summary>
    public async Task<IUserMessage> PostToChannelAsync(IMessageChannel channel, Event evt, Embed? embed = null, bool pingRoles = true)
    {
        var message = await EventAnnouncement.SendAsync(channel, evt, embed, pingRoles);

        _slowQueue.Enqueue(nameof(EventAnnouncement.AddReactionsAsync), _ => EventAnnouncement.AddReactionsAsync(message, evt, _logger));

        return message;
    }
}
