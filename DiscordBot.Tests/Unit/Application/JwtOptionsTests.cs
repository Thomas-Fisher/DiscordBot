using DiscordBot.Application.Options;

namespace DiscordBot.Tests.Unit.Application;

public class JwtOptionsTests
{
    private const string PlaceholderSecret = "your-super-secret-jwt-key-min-32-characters-long";

    private static JwtOptions Valid() => new()
    {
        Secret = new string('k', 40),
        Issuer = "issuer",
        Audience = "audience"
    };

    [Fact]
    public void Validate_CompleteSettings_HasNoErrors()
    {
        var errors = Valid().Validate(allowPlaceholderSecret: false);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    public void Validate_MissingOrShortSecret_IsAnError(string? secret)
    {
        var options = Valid();
        options.Secret = secret!;

        var errors = options.Validate(allowPlaceholderSecret: true);

        Assert.Contains(errors, e => e.Contains("Jwt:Secret"));
    }

    [Fact]
    public void Validate_MissingIssuerAudienceAndNonPositiveLifetimes_ReportsEach()
    {
        var options = Valid();
        options.Issuer = "";
        options.Audience = " ";
        options.ExpiryMinutes = 0;
        options.RefreshExpiryDays = -1;

        var errors = options.Validate(allowPlaceholderSecret: false);

        Assert.Equal(4, errors.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_ReplaceWithPlaceholderSecret_IsRefusedEvenInDevelopment(bool allowPlaceholderSecret)
    {
        var options = Valid();
        options.Secret = "REPLACE-WITH-A-LONG-RANDOM-STRING-OF-32-PLUS-CHARS";

        var errors = options.Validate(allowPlaceholderSecret);

        Assert.Contains(errors, e => e.Contains("placeholder"));
    }
}
