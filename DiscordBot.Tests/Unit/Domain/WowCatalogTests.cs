using DiscordBot.Domain.Enums;
using DiscordBot.Domain.Wow;

namespace DiscordBot.Tests.Unit.Domain;

public class WowCatalogTests
{
    [Fact]
    public void SpecsFor_EveryClass_HasOnlyLowerCaseSpecs()
    {
        var specsByClass = WowCatalog.Classes.ToDictionary(c => c, WowCatalog.SpecsFor);

        // Specs are compared and stored in lower case, so a mixed-case one would never match
        Assert.All(specsByClass, pair =>
        {
            Assert.NotEmpty(pair.Value);
            Assert.All(pair.Value, spec => Assert.Equal(spec.ToLowerInvariant(), spec));
        });
    }

    [Theory]
    [InlineData("mage", "fire", "mage", "fire")]
    [InlineData("MAGE", "Fire", "mage", "fire")]
    [InlineData("  Priest ", " HOLY ", "priest", "holy")]
    [InlineData("rogue", "Combat", "rogue", "combat")]
    public void TryNormalize_ValidPairInAnyCaseOrSpacing_ReturnsTheCanonicalNames(string wowClass, string spec, string expectedClass, string expectedSpec)
    {
        var valid = WowCatalog.TryNormalize(EventType.WowClassic, wowClass, spec, out var normalizedClass, out var normalizedSpec);

        Assert.True(valid);
        Assert.Equal(expectedClass, normalizedClass);
        Assert.Equal(expectedSpec, normalizedSpec);
    }

    [Theory]
    [InlineData("mage", "arms")]
    [InlineData("mage", "")]
    [InlineData("", "fire")]
    [InlineData(null, "fire")]
    [InlineData("mage", null)]
    [InlineData("evoker", "devastation")]
    [InlineData("rogue", "outlaw")]
    public void TryNormalize_SpecNotBelongingToTheClassOrBlank_IsRejectedWithEmptyOutputs(string? wowClass, string? spec)
    {
        var valid = WowCatalog.TryNormalize(EventType.WowClassic, wowClass, spec, out var normalizedClass, out var normalizedSpec);

        Assert.False(valid);
        Assert.Equal("", normalizedClass);
        Assert.Equal("", normalizedSpec);
    }

    [Fact]
    public void RoleOf_EverySpecOfEveryClass_HasARole()
    {
        var specsWithoutARole = WowCatalog.Classes
            .SelectMany(c => WowCatalog.SpecsFor(c).Select(spec => (Class: c, Spec: spec)))
            .Where(pair => WowCatalog.RoleOf(pair.Class, pair.Spec) == null)
            .ToList();

        // A spec without a role would never appear in the tank, healer or DPS columns
        Assert.Empty(specsWithoutARole);
    }

    [Theory]
    [InlineData("mage", null)]
    [InlineData(null, "fire")]
    [InlineData("mage", "arms")]
    public void RoleOf_MissingOrMismatchedClassAndSpec_HasNoRole(string? wowClass, string? spec)
    {
        var role = WowCatalog.RoleOf(wowClass, spec);

        Assert.Null(role);
    }

    [Theory]
    [InlineData("deathknight", "Death Knight")]
    [InlineData("warrior", "Warrior")]
    public void DisplayName_StoredClassName_IsReadableAsAPlayerWouldWriteIt(string stored, string expected)
    {
        var name = WowCatalog.DisplayName(stored);

        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData(EventType.WowClassic, true)]
    [InlineData(EventType.WowForever, false)]
    [InlineData(EventType.Generic, false)]
    public void ClassesFor_DeathKnightAndMonk_AreOnlyOfferedWhereTheyExist(EventType type, bool offered)
    {
        var classes = WowCatalog.ClassesFor(type);

        Assert.Equal((offered, offered), (classes.Contains("deathknight"), classes.Contains("monk")));
        Assert.Equal(type != EventType.Generic, classes.Contains("mage"));
    }

    [Fact]
    public void TryNormalize_DeathKnightOnAForeverEvent_IsRefused()
    {
        var accepted = WowCatalog.TryNormalize(EventType.WowForever, "deathknight", "blood", out _, out _);

        Assert.False(accepted);
    }

    [Theory]
    [InlineData(EventType.WowClassic)]
    [InlineData(EventType.WowForever)]
    public void SpecsFor_Rogue_OffersCombatAsAMeleeSpec(EventType type)
    {
        Assert.Contains("combat", WowCatalog.SpecsFor(type, "rogue"));
        Assert.Equal(WowRole.Melee, WowCatalog.RoleOf("rogue", "combat"));
    }
}
