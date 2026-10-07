using DiscordBot.Domain.Enums;

namespace DiscordBot.Domain.Entities;

/// <summary>
/// The spec a member last chose for a class, so signing up with that class again does not ask them. Kept per server and event type:
/// the same class can be played as a different spec in, say, a Classic raid and a Forever raid.
/// </summary>
public class RememberedSpec
{
    public ulong GuildId { get; set; }

    public ulong UserId { get; set; }

    public EventType EventType { get; set; }

    public string WowClass { get; set; } = null!;

    public string WowSpec { get; set; } = null!;

    public DateTime UpdatedAt { get; set; }
}
