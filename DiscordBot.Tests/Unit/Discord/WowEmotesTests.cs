using DiscordBot.Discord.Services.WoW;
using DiscordBot.Domain.Wow;

namespace DiscordBot.Tests.Unit.Discord;

public class WowEmotesTests
{
    [Theory]
    [InlineData("hunter", true)]
    [InlineData("HUNTER", true)]
    [InlineData("<:hunter:1553632766363701258>", true)]
    [InlineData("paladin_holy", true)]
    [InlineData("<:paladin_holy:1553632720196739113>", true)]
    [InlineData("👍", false)]
    [InlineData("huntsman", false)]
    public void IsWowClassEmote_BareNameOrFullEmoteForm_RecognisesClassAndSpecEmotes(string emote, bool expected)
    {
        var isWowEmote = WowEmotes.IsWowClassEmote(emote);

        Assert.Equal(expected, isWowEmote);
    }

    [Theory]
    [InlineData("<:mage:1553632767303098528>", "mage")]
    [InlineData("mage", "mage")]
    [InlineData("mage_fire", "")]
    [InlineData("👍", "")]
    public void GetClassNameFromEmote_OnlyAClassEmoteNamesAClass(string emote, string expectedClass)
    {
        var className = WowEmotes.GetClassNameFromEmote(emote);

        Assert.Equal(expectedClass, className);
    }

    [Fact]
    public void EmoteNames_EveryClassAndSpecInTheCatalog_HaveACustomEmote()
    {
        var emotes = WowCatalog.Classes
            .SelectMany(c => WowCatalog.SpecsFor(c).Select(s => WowEmotes.GetSpecEmoteName(c, s)).Append(WowEmotes.GetClassEmoteName(c)))
            .ToList();

        // An emote that is not listed silently falls back to plain text
        Assert.All(emotes, emote => Assert.StartsWith("<:", emote));
    }

    [Fact]
    public void IconUrls_EveryClassAndSpecInTheCatalog_HasAnIconOnDiscordsCdn()
    {
        var icons = WowEmotes.IconUrls();

        foreach (var className in WowCatalog.Classes)
        {
            Assert.Contains(className, icons.Keys);
            Assert.All(WowCatalog.SpecsFor(className), spec => Assert.Contains($"{className}_{spec}", icons.Keys));
        }

        Assert.All(icons.Values, url => Assert.Matches(@"^https://cdn\.discordapp\.com/emojis/\d+\.png$", url));
    }
}
