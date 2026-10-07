namespace DiscordBot.Contracts;

public sealed record GuildDetailsDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Icon { get; init; }
    public string? Description { get; init; }
    public int MemberCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public required string OwnerId { get; init; }

    public string? OwnerName { get; init; }
}
