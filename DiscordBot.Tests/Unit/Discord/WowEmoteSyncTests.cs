using DiscordBot.Discord.DiscordEventHandlers;
using DiscordBot.Discord.Services.WoW;
using DiscordBot.Domain.Wow;
using DiscordBot.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Unit.Discord;

public class WowEmoteSyncTests
{
    private sealed class FakeStore : IApplicationEmoteStore
    {
        private ulong _next = 5000;

        public Dictionary<string, ulong> Existing { get; } = new();

        public List<string> Created { get; } = new();

        public HashSet<string> FailOn { get; } = new();

        public Task<IReadOnlyDictionary<string, ulong>> ListAsync()
        {
            return Task.FromResult<IReadOnlyDictionary<string, ulong>>(new Dictionary<string, ulong>(Existing));
        }

        public Task<ulong> CreateAsync(string name, Stream image)
        {
            if (FailOn.Contains(name))
            {
                throw new InvalidOperationException("Discord refused the upload");
            }

            Assert.True(image.Length > 0);
            Created.Add(name);

            return Task.FromResult(_next++);
        }
    }

    private readonly FakeStore _store = new();
    private IReadOnlyDictionary<string, ulong> _registered = new Dictionary<string, ulong>();

    private Task Sync()
    {
        return new WowEmoteSync(_store, new ImmediateTaskQueue(), NullLogger<WowEmoteSync>.Instance, ids => _registered = ids).SyncAsync();
    }

    [Fact]
    public async Task SyncAsync_NoEmotesYet_UploadsEveryIconAndRegistersTheirIds()
    {
        await Sync();

        Assert.Equal(WowEmoteIcons.Names.Order(), _store.Created.Order());
        Assert.Equal(WowEmoteIcons.Names.Count, _registered.Count);
        Assert.All(WowEmoteIcons.Names, name => Assert.Contains(name, _registered.Keys));
    }

    [Fact]
    public async Task SyncAsync_SomeEmotesExist_UploadsOnlyTheMissingOnesAndKeepsTheExistingIds()
    {
        _store.Existing["mage"] = 42;
        _store.Existing["mage_fire"] = 43;

        await Sync();

        Assert.DoesNotContain("mage", _store.Created);
        Assert.DoesNotContain("mage_fire", _store.Created);
        Assert.Equal(WowEmoteIcons.Names.Count - 2, _store.Created.Count);
        Assert.Equal((42UL, 43UL), (_registered["mage"], _registered["mage_fire"]));
    }

    [Fact]
    public async Task SyncAsync_AllEmotesExist_UploadsNothing()
    {
        foreach (var name in WowEmoteIcons.Names)
        {
            _store.Existing[name] = 7;
        }

        await Sync();

        Assert.Empty(_store.Created);
        Assert.Equal(WowEmoteIcons.Names.Count, _registered.Count);
    }

    [Fact]
    public async Task SyncAsync_OneUploadFails_CarriesOnWithTheRest()
    {
        _store.FailOn.Add("hunter");

        await Sync();

        Assert.DoesNotContain("hunter", _registered.Keys);
        Assert.Equal(WowEmoteIcons.Names.Count - 1, _registered.Count);
    }

    [Fact]
    public void Icons_EveryClassAndSpecInTheCatalog_HasAnIconAndNothingIsSpare()
    {
        var expected = WowCatalog.Classes
            .SelectMany(c => WowCatalog.SpecsFor(c).Select(s => $"{c}_{s}").Append(c))
            .Order();

        Assert.Equal(expected, WowEmoteIcons.Names.Order());
    }
}
