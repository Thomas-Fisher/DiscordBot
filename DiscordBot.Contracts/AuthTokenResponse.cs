namespace DiscordBot.Contracts;

/// <summary>
/// The refresh token is deliberately not part of this response: it is sent as an HttpOnly cookie.
/// </summary>
public sealed record AuthTokenResponse
{
    public required string AccessToken { get; init; }
    public int ExpiresIn { get; init; }
    public required UserInfoDto User { get; init; }
}
