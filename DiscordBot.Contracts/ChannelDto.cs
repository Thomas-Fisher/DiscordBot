namespace DiscordBot.Contracts;

public sealed record ChannelDto
{
    public const int TextChannelType = 0;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public int Type { get; init; }
    public int Position { get; init; }
}
