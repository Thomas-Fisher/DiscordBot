using DiscordBot.Discord.Services;
using Microsoft.Extensions.Caching.Memory;
using DiscordBot.Domain.Enums;

namespace DiscordBot.Tests.Unit.Discord;

public class InteractionStateTests
{
    private readonly InteractionState _state = new(new MemoryCache(new MemoryCacheOptions()));

    private static PendingEvent Pending(string title) =>
        new(title, "desc", new DateTime(2026, 10, 1, 19, 30, 0, DateTimeKind.Utc), EventType.Generic, 30, null);

    [Fact]
    public void GetPendingEvent_AfterItWasCleared_ReturnsNothing()
    {
        _state.StorePendingEvent(10, 1, Pending("Raid night"));
        _state.ClearPendingEvent(10, 1);

        var pending = _state.GetPendingEvent(10, 1);

        Assert.Null(pending);
    }

    [Fact]
    public void GetPendingEvent_TwoPeopleCreatingEventsAtOnce_EachGetsTheirOwn()
    {
        _state.StorePendingEvent(10, 1, Pending("First person's event"));
        _state.StorePendingEvent(10, 2, Pending("Second person's event"));

        var first = _state.GetPendingEvent(10, 1);
        var second = _state.GetPendingEvent(10, 2);

        Assert.Equal(("First person's event", "Second person's event"), (first!.Title, second!.Title));
    }

    [Fact]
    public void GetPendingEvent_SamePersonCreatingInTwoServers_EachServerGetsItsOwn()
    {
        _state.StorePendingEvent(10, 1, Pending("Event in the first server"));
        _state.StorePendingEvent(20, 1, Pending("Event in the second server"));

        Assert.Equal("Event in the first server", _state.GetPendingEvent(10, 1)!.Title);
        Assert.Equal("Event in the second server", _state.GetPendingEvent(20, 1)!.Title);
    }

    [Fact]
    public void GetEventType_NothingStoredOrItWasCleared_IsNullSoTheCallerCanSayItExpired()
    {
        _state.StoreEventType(10, 1, EventType.WowClassic);

        Assert.Equal(EventType.WowClassic, _state.GetEventType(10, 1));
        Assert.Null(_state.GetEventType(20, 1));
        _state.ClearEventType(10, 1);
        Assert.Null(_state.GetEventType(10, 1));
    }
}
