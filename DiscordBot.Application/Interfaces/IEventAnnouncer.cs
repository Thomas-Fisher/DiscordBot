using DiscordBot.Domain.Entities;

namespace DiscordBot.Application.Interfaces;

public interface IEventAnnouncer
{
    /// <summary>
    /// Posts the event's announcement in the channel and returns the id of the message as soon as it exists. The signup reactions
    /// are added afterwards, in the background, so the caller can link the event to the message straight away; a reaction clicked
    /// before that link exists is ignored. Throws when it cannot post: the channel is not available, or the bot may not write there.
    /// </summary>
    Task<ulong> PostAsync(Event evt, ulong channelId);
}
