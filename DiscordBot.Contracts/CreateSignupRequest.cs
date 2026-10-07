namespace DiscordBot.Contracts;

public sealed record CreateSignupRequest
{
    public ulong UserId { get; init; }
    public string? WowClass { get; init; }
    public string? WowSpec { get; init; }
}
