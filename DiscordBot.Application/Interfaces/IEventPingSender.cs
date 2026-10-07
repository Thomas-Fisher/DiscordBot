using DiscordBot.Application.Models;
using DiscordBot.Domain.Entities;

namespace DiscordBot.Application.Interfaces;

public interface IEventPingSender
{
    /// <summary>
    /// Posts the ping in the event's channel as a reply to its announcement, mentioning the recipients. Throws when it cannot post:
    /// the channel is not available, or the bot may not write there.
    /// </summary>
    Task SendAsync(Event evt, PingTarget target, string? note, IReadOnlyList<PingRecipient> recipients);
}
