namespace DiscordBot.Contracts;

public sealed record OAuthCallbackRequest
{
    public string Code { get; init; } = "";
}
