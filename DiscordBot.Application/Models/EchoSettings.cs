namespace DiscordBot.Application.Models;

/// <summary>
/// A guild's logging preferences. Logs are always stored in the database; these only control the copy echoed into the
/// guild's own Discord channel.
/// </summary>
/// <param name="ChannelId">The channel logs are echoed into, if one has been chosen.</param>
/// <param name="Enabled">Whether logs are echoed into that channel.</param>
public sealed record EchoSettings(ulong? ChannelId, bool Enabled)
{
    /// <summary>The channel to post an echo into, or null when echoing is off or no channel is set.</summary>
    public ulong? ActiveChannelId => Enabled && ChannelId is > 0 ? ChannelId : null;
}
