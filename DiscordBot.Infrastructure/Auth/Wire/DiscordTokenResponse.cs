using System.Text.Json.Serialization;

namespace DiscordBot.Infrastructure.Auth.Wire;

/// <summary>What Discord's token endpoint returns. Only the access token is used: the app issues its own tokens after sign-in.</summary>
internal sealed class DiscordTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }
}
