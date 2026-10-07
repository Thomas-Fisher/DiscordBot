using Discord;
using Discord.WebSocket;
using DiscordBot.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DiscordBot.Discord.Presentation;

namespace DiscordBot.Discord.Services;

/// <summary>
/// Keeps an event's Discord message in step with its signups. Requests are debounced per message, so twenty people
/// reacting at once cause one edit (one fetch of the signups, one message fetch, one embed rebuild) instead of twenty.
/// </summary>
public class EventMessageRefresher : IEventMessageUpdater
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(1500);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DiscordSocketClient _client;
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly ILogger<EventMessageRefresher> _logger;
    private readonly RequestCoalescer<ulong> _debouncer;

    public EventMessageRefresher(
        IServiceScopeFactory scopeFactory,
        DiscordSocketClient client,
        IBackgroundTaskQueue backgroundQueue,
        ILogger<EventMessageRefresher> logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _backgroundQueue = backgroundQueue;
        _logger = logger;

        _debouncer = new RequestCoalescer<ulong>(DebounceWindow, messageId =>
            _backgroundQueue.Enqueue(nameof(EventMessageRefresher), _ => RefreshAsync(messageId)));
    }

    public Task RequestRefreshAsync(ulong messageId)
    {
        _debouncer.Request(messageId);

        return Task.CompletedTask;
    }

    public Task RequestRemovalAsync(ulong channelId, ulong messageId)
    {
        _backgroundQueue.Enqueue(nameof(RequestRemovalAsync), async _ =>
        {
            if (_client.GetChannel(channelId) is IMessageChannel channel)
            {
                try
                {
                    await channel.DeleteMessageAsync(messageId);
                }
                catch (Exception ex) when (DiscordErrors.IsPermanent(ex))
                {
                    // Already deleted, or the bot may not delete messages there: the event is deleted either way
                    _logger.LogDebug(ex, "Could not delete the announcement {MessageId} in channel {ChannelId}", messageId, channelId);
                }
            }
        });

        return Task.CompletedTask;
    }

    private async Task RefreshAsync(ulong messageId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var evt = await scope.ServiceProvider.GetRequiredService<IEventService>().FindByMessageAsync(messageId);
            if (evt == null || _client.GetChannel(evt.ChannelId ?? 0) is not IMessageChannel channel)
            {
                return;
            }

            var signups = await scope.ServiceProvider.GetRequiredService<ISignupService>().GetSignupsAsync(evt.EventId);

            if (await channel.GetMessageAsync(messageId) is not IUserMessage message)
            {
                return;
            }

            var embed = EventEmbedBuilder.Build(evt, signups, GuildMemberNames.For(_client, evt.GuildId), message.GetJumpUrl());
            await message.ModifyAsync(m => m.Embed = embed);

            _logger.LogInformation("Updated event message {MessageId} with signup info", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating event message {MessageId}", messageId);
        }
    }
}
