using DiscordBot.Application.Options;

namespace DiscordBot.Api;

/// <summary>The dashboard's address(es). The refresh token cookie is sent cross-origin, so the origins have to be listed, never "any".</summary>
public sealed class CorsSettings : IValidatableOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];

    public IReadOnlyList<string> Validate()
    {
        var invalid = AllowedOrigins
            .Where(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.PathAndQuery != "/")
            .ToList();

        return invalid.Count == 0
            ? []
            : [$"Cors:AllowedOrigins must be plain origins such as https://dashboard.example.com (no path, and not \"*\"): {string.Join(", ", invalid)}"];
    }
}
