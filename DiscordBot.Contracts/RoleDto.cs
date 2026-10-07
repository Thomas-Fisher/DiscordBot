namespace DiscordBot.Contracts;

public sealed record RoleDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int Position { get; init; }
}
