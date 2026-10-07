using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DiscordBot.Application.Telemetry;

/// <summary>
/// The one source of traces and the one meter the bot publishes. They cost next to nothing until something listens
/// (OpenTelemetry in the API host, or a test), and the layers use them without knowing what, if anything, exports them.
/// </summary>
public static class BotTelemetry
{
    public const string Name = "DiscordBot";

    public static readonly ActivitySource Source = new(Name);

    public static readonly Meter Meter = new(Name);
}
