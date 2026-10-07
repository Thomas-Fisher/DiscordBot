using DiscordBot.Domain.Enums;

namespace DiscordBot.Domain.Entities;

public class EventSignup
{
    public ulong SignupId { get; set; }
    public ulong EventId { get; set; }
    public ulong UserId { get; set; }
    public SignupStatus Status { get; set; } = SignupStatus.SignedUp;
    public DateTime SignedUpAt { get; set; }
    public DateTime? SignedOffAt { get; set; }

    public string? WowClass { get; set; }
    public string? WowSpec { get; set; }
    public int? SignupOrder { get; set; }

    /// <summary>The raid group (1 to <see cref="Common.RosterRules.MaxGroups"/>) this person was put in, or null while they are in none. Only people who are signed up or unsure have one; signing off removes it.</summary>
    public int? GroupNumber { get; set; }

    public Event? Event { get; set; }
}
