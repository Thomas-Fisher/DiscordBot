using DiscordBot.Application.Models;

namespace DiscordBot.Application.Interfaces;

public interface IDiscordOAuthClient
{
    /// <summary>Trades the authorisation code for the user's Discord access token. Null when Discord refuses the code.</summary>
    Task<string?> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Who the access token belongs to. Throws when Discord cannot say.</summary>
    Task<DiscordUser> GetUserAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>The servers the user is in, with what they may do there. Throws when Discord cannot say.</summary>
    Task<IReadOnlyList<DiscordGuildMembership>> GetGuildsAsync(string accessToken, CancellationToken cancellationToken = default);
}
