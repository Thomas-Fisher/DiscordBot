namespace DiscordBot.Contracts;

public sealed record SignupDetailsDto
{
    public ulong SignupId { get; init; }
    public ulong UserId { get; init; }

    /// <summary>What the server shows for this user. Null when they are no longer in the server.</summary>
    public string? DisplayName { get; init; }

    public required string Status { get; init; }
    public string? WowClass { get; init; }
    public string? WowSpec { get; init; }
    public int? SignupOrder { get; init; }

    /// <summary>The raid group (1 to 8) this person was put in. Null while they are in no group.</summary>
    public int? Group { get; init; }
    public DateTime SignedUpAt { get; init; }
    public DateTime? SignedOffAt { get; init; }
}
