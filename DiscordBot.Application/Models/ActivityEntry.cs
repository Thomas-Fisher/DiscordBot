using DiscordBot.Domain.Enums;

namespace DiscordBot.Application.Models;

/// <summary>One thing that happened, for the activity log. <paramref name="Changes"/> is stored as JSON.</summary>
public sealed record ActivityEntry(
    ActivityActionType Action,
    ActivityEntity Entity,
    ulong? EntityId,
    ulong? GuildId,
    ulong? UserId,
    ActivityOrigin Origin,
    object? Changes = null);
