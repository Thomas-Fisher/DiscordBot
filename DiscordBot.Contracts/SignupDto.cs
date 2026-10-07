namespace DiscordBot.Contracts;

public sealed record SignupDto
{
    public ulong UserId { get; init; }

    /// <summary>What the server shows for this user. Null when they are no longer in the server.</summary>
    public string? DisplayName { get; init; }

    public DateTime SignedUpAt { get; init; }
}
