namespace DiscordBot.Application.Interfaces;

/// <summary>
/// The outcome of a refresh. Without <see cref="Tokens"/>, <see cref="SessionEnded"/> says whether the caller's cookie is now
/// useless (unknown, expired or replayed) or merely lost a race with a newer request from the same browser, whose cookie must
/// be kept.
/// </summary>
public record RefreshResult(TokenPair? Tokens, bool SessionEnded)
{
    public static RefreshResult Refreshed(TokenPair tokens) => new(tokens, false);

    public static RefreshResult Ended() => new(null, true);

    public static RefreshResult Superseded() => new(null, false);
}
