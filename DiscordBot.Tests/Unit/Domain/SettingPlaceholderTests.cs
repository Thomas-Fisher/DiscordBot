using DiscordBot.Domain.Common;

namespace DiscordBot.Tests.Unit.Domain;

public class SettingPlaceholderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("REPLACE-WITH-YOUR-CLIENT-ID (Discord developer portal)")]
    [InlineData("  replace-with-your-bot-token")]
    public void IsUnset_BlankOrPlaceholder_IsTrue(string? value)
    {
        Assert.True(SettingPlaceholder.IsUnset(value));
    }

    [Theory]
    [InlineData("628362748828450816")]
    [InlineData("a-real-looking-token")]
    public void IsUnset_ARealValue_IsFalse(string value)
    {
        Assert.False(SettingPlaceholder.IsUnset(value));
    }
}
