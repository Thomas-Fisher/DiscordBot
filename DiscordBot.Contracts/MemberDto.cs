namespace DiscordBot.Contracts;

public sealed record MemberDto
{
    public ulong Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Username { get; init; }
    public string? AvatarUrl { get; init; }
}
