using Discord;
using Discord.WebSocket;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Discord.Presentation;
using DiscordBot.Domain.Entities;

namespace DiscordBot.Discord.Services;

public class DiscordEventPingSender : IEventPingSender
{
    private readonly DiscordSocketClient _client;

    public DiscordEventPingSender(DiscordSocketClient client)
    {
        _client = client;
    }

    public async Task SendAsync(Event evt, PingTarget target, string? note, IReadOnlyList<PingRecipient> recipients)
    {
        if (evt.ChannelId is not { } channelId || evt.MessageId is not { } messageId || _client.GetChannel(channelId) is not IMessageChannel channel)
        {
            throw new InvalidOperationException($"The channel of event {evt.EventId} is not available to the bot");
        }

        // The roster is laid out as an embed under the first message; the message around it is what notifies the people
        var roster = target == PingTarget.Roster ? EventRosterEmbed.Build(evt, recipients, GuildMemberNames.For(_client, evt.GuildId)) : null;

        var first = true;
        foreach (var chunk in EventPingMessage.Build(evt, target, note, recipients))
        {
            // The first message answers the announcement, so it is easy to find; the rest follow it. Only the people listed are notified
            await channel.SendMessageAsync(
                chunk.Text,
                embed: first ? roster : null,
                allowedMentions: new AllowedMentions { UserIds = chunk.UserIds.ToList() },
                messageReference: first ? new MessageReference(messageId, channelId, evt.GuildId, failIfNotExists: false) : null);
            first = false;
        }
    }
}
