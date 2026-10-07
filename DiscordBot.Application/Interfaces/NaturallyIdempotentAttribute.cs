namespace DiscordBot.Application.Interfaces;

/// <summary>
/// Marks a handler that is safe to run twice for one event. Every other handler is recorded per (event, handler) and skipped
/// when the event is delivered again.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NaturallyIdempotentAttribute : Attribute
{
}
