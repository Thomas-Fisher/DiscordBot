namespace DiscordBot.Contracts;

public sealed record EventCreatedDto
{
    public ulong EventId { get; init; }
}
