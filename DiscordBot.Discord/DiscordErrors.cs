using System.Net;
using Discord.Net;

namespace DiscordBot.Discord;

/// <summary>
/// Tells Discord failures that retrying can never fix from the ones that might pass. A handler swallows only the first kind (after
/// logging it); anything else is thrown so the event is retried, and eventually dead-lettered, instead of being lost.
/// </summary>
public static class DiscordErrors
{
    /// <summary>True when the bot lacks the permission, or what it was acting on (a channel, a message, a user) no longer exists.</summary>
    public static bool IsPermanent(Exception exception)
    {
        return exception is HttpException { HttpCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound };
    }
}
