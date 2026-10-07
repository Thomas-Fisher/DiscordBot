using Microsoft.Extensions.Caching.Memory;
using DiscordBot.Domain.Enums;

namespace DiscordBot.Discord.Services;

/// <summary>
/// Remembers what someone chose in earlier steps of a multi-step Discord flow (pick an event type, then fill in a form; click a
/// class, then pick a spec). Entries expire, and the store is safe to use from several interactions at once.
/// </summary>
public class InteractionState
{
    private static readonly TimeSpan CreationFlowLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SpecSelectionLifetime = TimeSpan.FromHours(1);

    private readonly IMemoryCache _cache;

    public InteractionState(IMemoryCache cache)
    {
        _cache = cache;
    }

    // The creating-an-event flow is remembered per server as well as per person: the same person can be part-way through it in two
    // servers, and each must get their own answers back.

    public void StoreEventType(ulong guildId, ulong userId, EventType type)
    {
        _cache.Set(EventTypeKey(guildId, userId), type, CreationFlowLifetime);
    }

    /// <summary>The event type picked earlier, or null when there is none (it expired, or the flow was never started).</summary>
    public EventType? GetEventType(ulong guildId, ulong userId)
    {
        return _cache.TryGetValue(EventTypeKey(guildId, userId), out EventType type) ? type : null;
    }

    public void ClearEventType(ulong guildId, ulong userId)
    {
        _cache.Remove(EventTypeKey(guildId, userId));
    }

    public void StorePendingEvent(ulong guildId, ulong userId, PendingEvent pending)
    {
        _cache.Set(PendingKey(guildId, userId), pending, CreationFlowLifetime);
    }

    public PendingEvent? GetPendingEvent(ulong guildId, ulong userId)
    {
        return _cache.TryGetValue(PendingKey(guildId, userId), out PendingEvent? pending) ? pending : null;
    }

    public void ClearPendingEvent(ulong guildId, ulong userId)
    {
        _cache.Remove(PendingKey(guildId, userId));
    }

    public void StoreSpecSelection(SpecSelectionContext context)
    {
        _cache.Set(SpecKey(context.UserId, context.MessageId), context, SpecSelectionLifetime);
    }

    public SpecSelectionContext? GetSpecSelection(ulong userId, ulong messageId)
    {
        return _cache.TryGetValue(SpecKey(userId, messageId), out SpecSelectionContext? context) ? context : null;
    }

    public void RemoveSpecSelection(ulong userId, ulong messageId)
    {
        _cache.Remove(SpecKey(userId, messageId));
    }

    private static string EventTypeKey(ulong guildId, ulong userId) => $"interaction:event-type:{guildId}:{userId}";

    private static string PendingKey(ulong guildId, ulong userId) => $"interaction:pending-event:{guildId}:{userId}";

    private static string SpecKey(ulong userId, ulong messageId) => $"interaction:spec:{userId}:{messageId}";
}
