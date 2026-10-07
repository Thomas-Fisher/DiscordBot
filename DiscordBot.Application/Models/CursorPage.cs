namespace DiscordBot.Application.Models;

public record CursorPage<T>(IReadOnlyList<T> Items, ActivityLogCursor? Next);
