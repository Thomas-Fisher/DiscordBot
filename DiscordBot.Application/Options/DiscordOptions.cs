using DiscordBot.Domain.Common;

namespace DiscordBot.Application.Options;

public class DiscordOptions : IValidatableOptions
{
    public const string SectionName = "Discord";

    /// <summary>False keeps the Discord connection off, so the API can run (and be tested) without a bot.</summary>
    public bool Enabled { get; set; } = true;

    public CommandScope CommandScope { get; set; } = CommandScope.Global;

    public string BotToken { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "https://discord.com/api/v10";

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Enabled && SettingPlaceholder.IsUnset(BotToken))
        {
            errors.Add("Discord:BotToken is missing: set it, or set Discord:Enabled to false to run without the bot");
        }

        Require(errors, "Discord:ClientId", ClientId);
        Require(errors, "Discord:ClientSecret", ClientSecret);
        RequireUrl(errors, "Discord:RedirectUri", RedirectUri);
        RequireUrl(errors, "Discord:ApiBaseUrl", ApiBaseUrl);

        return errors;
    }

    private static void Require(List<string> errors, string setting, string value)
    {
        if (SettingPlaceholder.IsUnset(value))
        {
            errors.Add($"{setting} is required: replace the placeholder with the value from the Discord developer portal (README section 3)");
        }
    }

    private static void RequireUrl(List<string> errors, string setting, string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            errors.Add($"{setting} must be a full URL");
        }
    }
}
