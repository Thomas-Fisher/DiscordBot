namespace DiscordBot.Application.Interfaces;

public record TokenPair(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    string UserId,
    string Username,
    string? Avatar);
