namespace DiscordBot.Contracts;

/// <summary>
/// Who to ping about an event: <see cref="Target"/> is Roster (everyone in a raid group), Standby (signed up but in no group) or
/// NoResponse (members of <see cref="RoleIds"/> who have not answered; the event's own pinged roles when not given).
/// </summary>
public sealed record PingRequestDto
{
    public string Target { get; init; } = "NoResponse";
    public List<ulong>? RoleIds { get; init; }

    /// <summary>For NoResponse: also ping people who answered unsure.</summary>
    public bool IncludeUnsure { get; init; }

    /// <summary>Words to put at the top of the message. Up to 300 characters.</summary>
    public string? Note { get; init; }
}

public sealed record PingRecipientDto
{
    public ulong UserId { get; init; }
    public string? DisplayName { get; init; }

    public int? Group { get; init; }
}

public sealed record PingPreviewDto
{
    public List<PingRecipientDto> Recipients { get; init; } = new();

    /// <summary>When the same people were last pinged for this event, if that was in the last few minutes.</summary>
    public DateTime? RecentlyPingedAt { get; init; }
}

public sealed record PingResultDto
{
    public int Count { get; init; }
}
