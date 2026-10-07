namespace DiscordBot.Application.Interfaces;

/// <summary>Keeps an event's Discord announcement in step with the event. Requests are batched and carried out by the bot's Discord connection.</summary>
public interface IEventMessageUpdater
{
    /// <summary>Asks for the announcement message to be re-rendered soon. Cheap to call; requests are batched.</summary>
    Task RequestRefreshAsync(ulong messageId);

    /// <summary>Asks for the announcement message to be deleted (when its event is deleted). Best effort: it may already be gone.</summary>
    Task RequestRemovalAsync(ulong channelId, ulong messageId);
}
