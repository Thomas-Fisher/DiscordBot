namespace DiscordBot.Contracts;

public sealed record UserInfoDto
{
    public required string Id { get; init; }
    public required string Username { get; init; }
    public string? Avatar { get; init; }
    public required List<GuildDto> Guilds { get; init; }
}
