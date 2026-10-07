using DiscordBot.Domain.Entities;

namespace DiscordBot.Application.Models;

public sealed record ActivityLogPage(IReadOnlyList<ActivityLog> Items, ActivityLogCursor? Next, int PageSize);
