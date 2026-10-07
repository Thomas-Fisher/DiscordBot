using DiscordBot.Domain.Enums;
using DiscordBot.Tests.Support;

namespace DiscordBot.Tests.Unit.Persistence;

public class RememberedSpecRepositoryTests
{
    private const ulong Guild = 7;
    private const ulong User = 42;

    private readonly TestDb _db = new();

    [Fact]
    public async Task Find_NothingSaved_IsNull()
    {
        Assert.Null(await _db.RememberedSpecs.FindAsync(Guild, User, EventType.WowClassic, "mage"));
    }

    [Fact]
    public async Task Save_ThenFind_ReturnsTheSpec()
    {
        await _db.RememberedSpecs.SaveAsync(Guild, User, EventType.WowClassic, "mage", "fire");

        Assert.Equal("fire", await _db.RememberedSpecs.FindAsync(Guild, User, EventType.WowClassic, "mage"));
    }

    [Fact]
    public async Task Save_AgainForTheSameClass_ReplacesTheSpec()
    {
        await _db.RememberedSpecs.SaveAsync(Guild, User, EventType.WowClassic, "mage", "fire");

        await _db.RememberedSpecs.SaveAsync(Guild, User, EventType.WowClassic, "mage", "frost");

        Assert.Equal("frost", await _db.RememberedSpecs.FindAsync(Guild, User, EventType.WowClassic, "mage"));
    }

    [Fact]
    public async Task Save_IsKeptSeparatePerEventTypeClassUserAndServer()
    {
        await _db.RememberedSpecs.SaveAsync(Guild, User, EventType.WowClassic, "mage", "fire");

        await _db.RememberedSpecs.SaveAsync(Guild, User, EventType.WowForever, "mage", "frost");
        await _db.RememberedSpecs.SaveAsync(Guild, User, EventType.WowClassic, "priest", "holy");

        Assert.Equal("fire", await _db.RememberedSpecs.FindAsync(Guild, User, EventType.WowClassic, "mage"));
        Assert.Equal("frost", await _db.RememberedSpecs.FindAsync(Guild, User, EventType.WowForever, "mage"));
        Assert.Equal("holy", await _db.RememberedSpecs.FindAsync(Guild, User, EventType.WowClassic, "priest"));
        Assert.Null(await _db.RememberedSpecs.FindAsync(Guild, User + 1, EventType.WowClassic, "mage"));
        Assert.Null(await _db.RememberedSpecs.FindAsync(Guild + 1, User, EventType.WowClassic, "mage"));
    }

    [Fact]
    public async Task Delete_ForgetsOnlyThatClass()
    {
        await _db.RememberedSpecs.SaveAsync(Guild, User, EventType.WowClassic, "mage", "fire");
        await _db.RememberedSpecs.SaveAsync(Guild, User, EventType.WowClassic, "priest", "holy");

        await _db.RememberedSpecs.DeleteAsync(Guild, User, EventType.WowClassic, "mage");

        Assert.Null(await _db.RememberedSpecs.FindAsync(Guild, User, EventType.WowClassic, "mage"));
        Assert.Equal("holy", await _db.RememberedSpecs.FindAsync(Guild, User, EventType.WowClassic, "priest"));
    }
}
