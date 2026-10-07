using Discord;
using Discord.WebSocket;
using DiscordBot.Discord.Constants;
using DiscordBot.Discord.Services.WoW;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Wow;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord.Services;

public class DiscordSpecPrompter : ISpecPrompter
{
    private readonly InteractionState _state;
    private readonly DiscordSocketClient _client;
    private readonly ILogger<DiscordSpecPrompter> _logger;

    public DiscordSpecPrompter(InteractionState state, DiscordSocketClient client, ILogger<DiscordSpecPrompter> logger)
    {
        _state = state;
        _client = client;
        _logger = logger;
    }

    public async Task SendPickerAsync(Event evt, ulong userId, string className)
    {
        if (!evt.MessageId.HasValue)
        {
            return;
        }

        var specs = WowCatalog.SpecsFor(evt.Type, className);
        if (specs.Count == 0)
        {
            _logger.LogWarning("No specs found for class {ClassName}", className);
            return;
        }

        var messageId = evt.MessageId.Value;
        _state.StoreSpecSelection(new SpecSelectionContext(userId, messageId, evt.EventId, className, evt.GuildId));

        var channel = _client.GetChannel(evt.ChannelId ?? 0) as ITextChannel;
        if (channel == null)
        {
            _logger.LogWarning("Channel {ChannelId} not found for spec selection", evt.ChannelId);
            return;
        }

        // Straight after startup the member cache is empty, so fall back to asking Discord for the member
        var user = await channel.Guild.GetUserAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} is not a member of guild {GuildId}", userId, evt.GuildId);
            return;
        }

        var selectMenuBuilder = new SelectMenuBuilder()
            .WithCustomId($"{InteractionCustomIds.WowSpecSelectPrefix}{userId}_{messageId}")
            .WithPlaceholder($"Select your {className} spec")
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var spec in specs)
        {
            selectMenuBuilder.AddOption(spec, spec, description: $"Select {spec}");
        }

        var components = new ComponentBuilder()
            .WithSelectMenu(selectMenuBuilder)
            .Build();

        try
        {
            await user.SendMessageAsync(
                $"**{className.ToUpperInvariant()} - Spec Selection**\n" +
                $"Please select your spec for the event in <#{evt.ChannelId}>",
                components: components
            );
        }
        catch (Exception dmEx) when (DiscordErrors.IsPermanent(dmEx))
        {
            // Closed DMs (or the user left): nothing a retry can change
            _logger.LogWarning(dmEx, "Could not send the spec picker to user {UserId}", userId);
        }
    }

    public async Task RemoveClassReactionAsync(Event evt, ulong userId, string className)
    {
        try
        {
            if (_client.GetChannel(evt.ChannelId ?? 0) is not IMessageChannel channel || !evt.MessageId.HasValue)
            {
                _logger.LogWarning("Channel {ChannelId} not found for removing reaction", evt.ChannelId);
                return;
            }

            var message = await channel.GetMessageAsync(evt.MessageId.Value);
            if (message == null)
            {
                _logger.LogWarning("Message {MessageId} not found in channel {ChannelId}", evt.MessageId, evt.ChannelId);
                return;
            }

            var emoteString = WowEmotes.GetClassEmoteName(className);
            await message.RemoveReactionAsync(Emote.Parse(emoteString), userId);

            _logger.LogDebug("Removed {Emoji} reaction from message {MessageId} for user {UserId}", emoteString, evt.MessageId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing class emoji reaction from message {MessageId}", evt.MessageId);
        }
    }
}
