using DiscordBot.Domain.Enums;

namespace DiscordBot.Application.Interfaces;

/// <summary>The spec each member last chose for a class, per server and event type (see <see cref="Domain.Entities.RememberedSpec"/>).</summary>
public interface IRememberedSpecRepository
{
    /// <summary>The spec the member last chose for the class, or null when they have not chosen one.</summary>
    Task<string?> FindAsync(ulong guildId, ulong userId, EventType eventType, string wowClass);

    /// <summary>Remembers the spec for the class, replacing an earlier choice.</summary>
    Task SaveAsync(ulong guildId, ulong userId, EventType eventType, string wowClass, string wowSpec);

    /// <summary>Forgets the member's spec for the class, so they are asked again.</summary>
    Task DeleteAsync(ulong guildId, ulong userId, EventType eventType, string wowClass);
}
