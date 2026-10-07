namespace DiscordBot.Application.Models;

public enum PingTarget
{
    /// <summary>Everyone who was put in a raid group.</summary>
    Roster,

    /// <summary>People who are signed up but not in a group.</summary>
    Standby,

    /// <summary>Members of the chosen roles who have not answered the event at all.</summary>
    NoResponse
}

/// <param name="Target">Who to ping.</param>
/// <param name="RoleIds">For <see cref="PingTarget.NoResponse"/>: the roles whose members are expected to answer. The event's own pinged roles when not given.</param>
/// <param name="IncludeUnsure">For <see cref="PingTarget.NoResponse"/>: also ping people who answered unsure.</param>
/// <param name="Note">Words to put at the top of the message.</param>
public sealed record PingRequest(PingTarget Target, IReadOnlyCollection<ulong>? RoleIds = null, bool IncludeUnsure = false, string? Note = null);

/// <summary>One person to ping, with their raid group, class, spec and whether they answered unsure when they have a signup (what the roster shows).</summary>
public sealed record PingRecipient(ulong UserId, int? Group, string? WowClass = null, string? WowSpec = null, bool IsUnsure = false);

/// <param name="Recipients">Everyone who would be pinged, in the order they would be listed.</param>
/// <param name="RecentlyPingedAt">When the same people were last pinged for this event, if that was only a few minutes ago.</param>
public sealed record PingPreview(IReadOnlyList<PingRecipient> Recipients, DateTime? RecentlyPingedAt);
