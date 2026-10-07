namespace DiscordBot.Contracts;

public sealed record EventDto : EventDtoBase
{
    public int SignedUpCount { get; init; }

    public int UnsureCount { get; init; }
}
