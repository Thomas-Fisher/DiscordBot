namespace DiscordBot.Contracts;

public sealed record GuildDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Icon { get; init; }
    public bool IsAdmin { get; init; }
}
