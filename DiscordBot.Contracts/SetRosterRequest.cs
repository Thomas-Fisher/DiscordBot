namespace DiscordBot.Contracts;

/// <summary>The whole roster of a WoW event: who is in which group. Anyone who is signed up or unsure but not listed is in no group.</summary>
public sealed record SetRosterRequest
{
    public List<RosterAssignment> Assignments { get; init; } = new();
}

public sealed record RosterAssignment
{
    public ulong UserId { get; init; }

    /// <summary>1 to 8, with five people at most in each group.</summary>
    public int Group { get; init; }
}
