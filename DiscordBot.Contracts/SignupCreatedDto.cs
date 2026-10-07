namespace DiscordBot.Contracts;

public sealed record SignupCreatedDto
{
    public required ulong UserId { get; init; }

    /// <summary>Where in the sign-up order this signup landed; empty when it did not join the order (a waiting list or a declined status).</summary>
    public int? Order { get; init; }
}
