using DiscordBot.Application.Errors;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Entities;

namespace DiscordBot.Application.Interfaces;

public interface IEventCreator
{
    /// <summary>
    /// Creates the event and posts its announcement in the channel. When the announcement cannot be posted the event is removed
    /// again and <see cref="EventErrors.AnnouncementFailed"/> is returned, since an event nobody can see or sign up to is useless.
    /// </summary>
    Task<Result<Event>> CreateAndAnnounceAsync(NewEventRequest request, ulong channelId, ActivityContext context);
}
