namespace DiscordBot.Contracts;

public sealed record GuildStatisticsDto
{
    public ulong GuildId { get; init; }
    public required string GuildName { get; init; }
    public int MemberCount { get; init; }
    public int TotalEvents { get; init; }
    public int UpcomingEvents { get; init; }
    public int RolesCount { get; init; }
    public int ChannelsCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public int BoostLevel { get; init; }
}
