namespace DiscordBot.Contracts;

public sealed record PaginatedActivityLogsDto
{
    public required List<ActivityLogDto> Logs { get; init; }
    public int PageSize { get; init; }

    /// <summary>Pass as <c>cursor</c> to get the next (older) page. Null when this is the last page.</summary>
    public string? NextCursor { get; init; }
}
