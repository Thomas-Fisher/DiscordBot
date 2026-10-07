using Discord;
using Discord.WebSocket;
using DiscordBot.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord;

public class DiscordMessageSender : IDiscordMessageSender
{
    private readonly DiscordSocketClient _client;
    private readonly ILogger<DiscordMessageSender> _logger;

    public DiscordMessageSender(DiscordSocketClient client, ILogger<DiscordMessageSender> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task SendMessageAsync(ulong channelId, string content)
    {
        try
        {
            if (_client.GetChannel(channelId) is not IMessageChannel channel)
            {
                _logger.LogWarning("Channel {ChannelId} not found or is not a message channel", channelId);
                return;
            }

            // Log text can contain mentions (including @everyone); showing them is fine, notifying anyone is not
            await channel.SendMessageAsync(content, allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex) when (DiscordErrors.IsPermanent(ex))
        {
            _logger.LogWarning(ex, "Cannot send to channel {ChannelId} (missing permission, or it no longer exists)", channelId);
        }
    }
}
