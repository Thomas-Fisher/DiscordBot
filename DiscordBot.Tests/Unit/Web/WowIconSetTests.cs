using DiscordBot.Web.Services;

namespace DiscordBot.Tests.Unit.Web;

public class WowIconSetTests
{
    private readonly WowIconSet _icons = new(new Dictionary<string, string>
    {
        ["paladin"] = "class.png",
        ["paladin_holy"] = "spec.png"
    });

    [Fact]
    public void ForPerson_SpecHasAnIcon_UsesTheSpecIcon()
    {
        Assert.Equal("spec.png", _icons.ForPerson("Paladin", "holy"));
    }

    [Fact]
    public void ForPerson_SpecHasNoIcon_FallsBackToTheClassIcon()
    {
        Assert.Equal("class.png", _icons.ForPerson("paladin", "protection"));
        Assert.Equal("class.png", _icons.ForPerson("paladin", null));
    }

    [Fact]
    public void ForPerson_NoIconsOrNoClass_GivesNull()
    {
        Assert.Null(WowIconSet.None.ForPerson("paladin", "holy"));
        Assert.Null(_icons.ForPerson(null, null));
        Assert.Null(_icons.ForClass("mage"));
    }
}
