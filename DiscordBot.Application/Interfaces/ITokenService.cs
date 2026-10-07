using DiscordBot.Application.Models;

namespace DiscordBot.Application.Interfaces;

public interface ITokenService
{
    Task<TokenPair> IssueAsync(DiscordUser user);

    /// <summary>Exchanges a refresh token for a new pair and revokes the old refresh token.</summary>
    Task<RefreshResult> RefreshAsync(string refreshToken);

    /// <summary>Revokes the session that the refresh token belongs to. Unknown tokens are ignored.</summary>
    Task RevokeAsync(string refreshToken);
}
