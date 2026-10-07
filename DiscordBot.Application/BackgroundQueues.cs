namespace DiscordBot.Application;

/// <summary>
/// The background queues, each with its own worker so slow work in one cannot hold up another. The default is <see
/// cref="Work"/>; ask for a lane with <c>[FromKeyedServices(BackgroundQueues.Publish)]</c>.
/// </summary>
public static class BackgroundQueues
{
    /// <summary>Publishing events to the event bus, in order (events with the same key must be published in the order they happened).</summary>
    public const string Publish = "publish";

    /// <summary>Everyday Discord work: refreshing event messages, tidying reactions.</summary>
    public const string Work = "work";

    /// <summary>Long jobs started when the bot connects: syncing guilds, catching up on reactions missed while offline.</summary>
    public const string Maintenance = "maintenance";

    public static readonly IReadOnlyList<string> All = new[] { Publish, Work, Maintenance };
}
