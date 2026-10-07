namespace DiscordBot.Contracts;

public sealed record ClientConfigDto
{
    public required string DiscordClientId { get; init; }
}
