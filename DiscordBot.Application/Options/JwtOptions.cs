using DiscordBot.Domain.Common;

namespace DiscordBot.Application.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = null!;
    public string Issuer { get; set; } = null!;
    public string Audience { get; set; } = null!;
    public int ExpiryMinutes { get; set; } = 60;
    public int RefreshExpiryDays { get; set; } = 7;

    /// <summary>How long a login can last in all, however often it is renewed.</summary>
    public int MaxSessionDays { get; set; } = 30;

    public const int MinSecretBytes = 32;

    // The secret shipped in appsettings.json so the project runs locally; anyone can read it, so it must never sign real tokens
    private const string PlaceholderSecretPrefix = "your-super-secret";

    /// <param name="allowPlaceholderSecret">True in Development, where the shipped placeholder secret is acceptable.</param>
    public IReadOnlyList<string> Validate(bool allowPlaceholderSecret)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Secret) || System.Text.Encoding.UTF8.GetByteCount(Secret) < MinSecretBytes)
        {
            errors.Add($"Jwt:Secret must be at least {MinSecretBytes} bytes");
        }
        else if (SettingPlaceholder.IsUnset(Secret))
        {
            errors.Add("Jwt:Secret is still a REPLACE-WITH placeholder; set your own (Jwt__Secret in .env)");
        }
        else if (!allowPlaceholderSecret && Secret.StartsWith(PlaceholderSecretPrefix, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Jwt:Secret is still the placeholder from appsettings.json; set your own (Jwt__Secret in .env)");
        }

        if (string.IsNullOrWhiteSpace(Issuer))
        {
            errors.Add("Jwt:Issuer is required");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            errors.Add("Jwt:Audience is required");
        }

        if (ExpiryMinutes <= 0)
        {
            errors.Add("Jwt:ExpiryMinutes must be greater than 0");
        }

        if (RefreshExpiryDays <= 0)
        {
            errors.Add("Jwt:RefreshExpiryDays must be greater than 0");
        }

        if (MaxSessionDays < RefreshExpiryDays)
        {
            errors.Add("Jwt:MaxSessionDays cannot be less than Jwt:RefreshExpiryDays");
        }

        return errors;
    }
}
