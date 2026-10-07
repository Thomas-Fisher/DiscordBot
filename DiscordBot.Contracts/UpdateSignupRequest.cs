namespace DiscordBot.Contracts;

public sealed record UpdateSignupRequest
{
    public string? WowClass { get; init; }
    public string? WowSpec { get; init; }
    public string? Status { get; init; }
}
