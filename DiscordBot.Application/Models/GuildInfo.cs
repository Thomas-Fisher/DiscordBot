namespace DiscordBot.Application.Models;

public sealed record GuildInfo(
    ulong Id,
    string Name,
    string? IconId,
    string? Description,
    int MemberCount,
    DateTime CreatedAt,
    ulong OwnerId,
    int BoostLevel,
    int RoleCount,
    int ChannelCount);
