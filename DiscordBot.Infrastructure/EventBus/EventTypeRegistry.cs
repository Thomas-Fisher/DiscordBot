using DiscordBot.Domain.Events;

namespace DiscordBot.Infrastructure.EventBus;

/// <summary>
/// The stable, versioned names events go by on the wire. Names are listed explicitly (not taken from the .NET type name)
/// so renaming or moving a class never breaks messages already in a topic. Bump the version suffix for a breaking change.
/// </summary>
public static class EventTypeRegistry
{
    private static readonly IReadOnlyDictionary<string, Type> TypesByName = new Dictionary<string, Type>
    {
        ["message-received.v1"] = typeof(MessageReceivedEvent),
        ["reaction-added.v1"] = typeof(ReactionAddedEvent),
        ["activity-log-created.v1"] = typeof(ActivityLogCreatedEvent)
    };

    private static readonly IReadOnlyDictionary<Type, string> NamesByType =
        TypesByName.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

    public static IReadOnlyCollection<Type> RegisteredTypes => NamesByType.Keys.ToList();

    public static string GetName(Type eventType)
    {
        return NamesByType.TryGetValue(eventType, out var name)
            ? name
            : throw new InvalidOperationException($"Event type {eventType.Name} is not registered in EventTypeRegistry");
    }

    public static bool TryResolve(string name, out Type eventType)
    {
        return TypesByName.TryGetValue(name, out eventType!);
    }
}
