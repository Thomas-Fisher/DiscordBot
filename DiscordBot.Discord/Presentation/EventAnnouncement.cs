using DiscordBot.Domain.Wow;
using Discord;
using DiscordBot.Discord.Services.WoW;
using DiscordBot.Domain.Entities;
using Microsoft.Extensions.Logging;
using DiscordBot.Discord.Constants;

namespace DiscordBot.Discord.Presentation;

/// <summary>
/// Builds and posts a new event's announcement, the same way for one-off events and every recurring occurrence. Sending the message
/// and adding its signup reactions are separate steps: the reactions take many seconds (Discord limits how fast they can be added), and
/// until the event is linked to the message anyone who clicks one is ignored, so the caller links the message in between.
/// </summary>
public static class EventAnnouncement
{
    public static Embed BuildEmbed(Event evt)
    {
        // A brand-new event has no signups yet, so this is the same layout later refreshes produce
        return EventEmbedBuilder.Build(evt, Array.Empty<EventSignup>(), _ => null);
    }

    /// <summary>
    /// Sends the announcement message, without its reactions. The roles chosen for the event are shown above the embed and, when
    /// <paramref name="pingRoles"/> is set, notified; a re-post of an existing announcement (moving it) shows them without pinging
    /// anyone again.
    /// </summary>
    public static async Task<IUserMessage> SendAsync(IMessageChannel channel, Event evt, Embed? embed = null, bool pingRoles = true)
    {
        var mentions = evt.MentionRoleIds.Select(id => $"<@&{id}>").ToList();
        var allowedMentions = pingRoles && mentions.Count > 0
            ? new AllowedMentions { RoleIds = evt.MentionRoleIds.ToList() }
            : AllowedMentions.None;

        var message = await channel.SendMessageAsync(
            text: mentions.Count > 0 ? string.Join(" ", mentions) : null,
            embed: embed ?? BuildEmbed(evt),
            allowedMentions: allowedMentions);

        return message;
    }

    public static async Task AddReactionsAsync(IUserMessage message, Event evt, ILogger logger)
    {
        // For WoW events the class emotes come first; signing up happens by picking a class/spec
        if (evt.IsWow)
        {
            foreach (var className in WowCatalog.ClassesFor(evt.Type))
            {
                try
                {
                    var emoteString = WowEmotes.GetClassEmoteName(className);
                    if (Emote.TryParse(emoteString, out var emote))
                    {
                        await message.AddReactionAsync(emote);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to add reaction for class {Class}", className);
                }
            }
        }

        try
        {
            if (!evt.IsWow)
            {
                await message.AddReactionAsync(SignupEmojis.ToEmote(SignupEmojis.SignedUp));
            }

            await message.AddReactionAsync(SignupEmojis.ToEmote(SignupEmojis.Unsure));
            await message.AddReactionAsync(SignupEmojis.ToEmote(SignupEmojis.SignedOff));

            if (evt.IsWow)
            {
                await message.AddReactionAsync(SignupEmojis.ToEmote(SignupEmojis.ResetSpec));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to add status reactions to event message {MessageId}", message.Id);
        }
    }
}
