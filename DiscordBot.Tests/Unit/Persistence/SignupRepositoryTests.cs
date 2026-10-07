using DiscordBot.Application.Models;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using DiscordBot.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Tests.Unit.Persistence;

public class SignupRepositoryTests : IAsyncLifetime
{
    private const ulong EventId = 1;

    private readonly TestDb _db = new();

    // Signups belong to an event, and the database checks that it exists
    public Task InitializeAsync() => _db.Events.CreateEventAsync(TestEvents.Wow(EventId));

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SignUpMagesAsync(int count)
    {
        for (ulong user = 1; user <= (ulong)count; user++)
        {
            await _db.Signups.SignUpAsync(EventId, user, "mage", "fire");
        }
    }

    [Fact]
    public async Task SetRoster_PeopleListed_AreGivenTheirGroupAndEveryoneElseHasNone()
    {
        await SignUpMagesAsync(3);

        var saved = await _db.Signups.SetRosterAsync(EventId, new Dictionary<ulong, int> { [1] = 1, [2] = 2 });

        Assert.True(saved);
        Assert.Equal(1, (await StoredAsync(1)).GroupNumber);
        Assert.Equal(2, (await StoredAsync(2)).GroupNumber);
        Assert.Null((await StoredAsync(3)).GroupNumber);
    }

    [Fact]
    public async Task SetRoster_SecondCall_ReplacesTheWholeRoster()
    {
        await SignUpMagesAsync(3);
        await _db.Signups.SetRosterAsync(EventId, new Dictionary<ulong, int> { [1] = 1, [2] = 1 });

        await _db.Signups.SetRosterAsync(EventId, new Dictionary<ulong, int> { [3] = 1 });

        Assert.Null((await StoredAsync(1)).GroupNumber);
        Assert.Null((await StoredAsync(2)).GroupNumber);
        Assert.Equal(1, (await StoredAsync(3)).GroupNumber);
    }

    [Fact]
    public async Task SetRoster_SomeoneListedIsNotSignedUp_ChangesNothing()
    {
        await SignUpMagesAsync(2);
        await _db.Signups.SetRosterAsync(EventId, new Dictionary<ulong, int> { [1] = 1 });
        await _db.Signups.SignOffAsync(EventId, 2, compactOrder: false);

        var saved = await _db.Signups.SetRosterAsync(EventId, new Dictionary<ulong, int> { [2] = 1, [99] = 1 });

        Assert.False(saved);
        Assert.Equal(1, (await StoredAsync(1)).GroupNumber);
        Assert.Null((await StoredAsync(2)).GroupNumber);
    }

    [Fact]
    public async Task SignOff_PersonInAGroup_LeavesTheGroup()
    {
        await SignUpMagesAsync(2);
        await _db.Signups.SetRosterAsync(EventId, new Dictionary<ulong, int> { [1] = 1, [2] = 1 });

        await _db.Signups.SignOffAsync(EventId, 1, compactOrder: true);

        Assert.Null((await StoredAsync(1)).GroupNumber);
        Assert.Equal(1, (await StoredAsync(2)).GroupNumber);
    }

    [Fact]
    public async Task SetRoster_PersonWhoAnsweredUnsure_CanBePutInAGroupAndKeepsItWhenTheyBecomeUnsureLater()
    {
        await SignUpMagesAsync(2);
        await _db.Signups.MarkUnsureAsync(EventId, 3, compactOrder: false);

        var saved = await _db.Signups.SetRosterAsync(EventId, new Dictionary<ulong, int> { [1] = 1, [3] = 1 });
        await _db.Signups.MarkUnsureAsync(EventId, 1, compactOrder: true);

        Assert.True(saved);
        Assert.Equal(1, (await StoredAsync(3)).GroupNumber);
        Assert.Equal(1, (await StoredAsync(1)).GroupNumber);
    }

    [Fact]
    public async Task SetRoster_PersonWhoSignedOff_CannotBePutInAGroup()
    {
        await SignUpMagesAsync(1);
        await _db.Signups.SignOffAsync(EventId, 1, compactOrder: false);

        var saved = await _db.Signups.SetRosterAsync(EventId, new Dictionary<ulong, int> { [1] = 1 });

        Assert.False(saved);
    }

    [Fact]
    public async Task SignUp_PersonWhoLeftAGroupAndCameBack_StartsOnStandby()
    {
        await SignUpMagesAsync(1);
        await _db.Signups.SetRosterAsync(EventId, new Dictionary<ulong, int> { [1] = 1 });
        await _db.Signups.SignOffAsync(EventId, 1, compactOrder: true);

        await _db.Signups.SignUpAsync(EventId, 1, "mage", "fire");

        Assert.Null((await StoredAsync(1)).GroupNumber);
    }

    [Fact]
    public async Task GetSignupCounts_MixOfStatuses_CountsSignedUpAndUnsureSeparatelyPerEventAndIgnoresSignedOff()
    {
        await _db.Events.CreateEventAsync(TestEvents.Wow(2));
        await SignUpMagesAsync(3);
        await _db.Signups.MarkUnsureAsync(EventId, 9, compactOrder: false);
        await _db.Signups.SignUpAsync(EventId, 8, "mage", "fire");
        await _db.Signups.SignOffAsync(EventId, 8, compactOrder: false);

        var counts = await _db.Signups.GetSignupCountsAsync(new ulong[] { EventId, 2, 3 });

        Assert.Equal(new SignupCounts(3, 1), counts[EventId]);
        Assert.False(counts.ContainsKey(2));
        Assert.False(counts.ContainsKey(3));
    }

    private async Task<EventSignup> StoredAsync(ulong userId)
    {
        using var context = _db.Open();
        return await context.EventSignups.AsNoTracking().SingleAsync(s => s.UserId == userId);
    }

    private async Task<List<int?>> SignedUpOrdersAsync()
    {
        using var context = _db.Open();
        return await context.EventSignups.AsNoTracking()
            .Where(s => s.Status == SignupStatus.SignedUp)
            .OrderBy(s => s.SignupOrder)
            .Select(s => s.SignupOrder)
            .ToListAsync();
    }

    [Fact]
    public async Task SignUp_SeveralPeopleWithASpec_GetSequentialPlacesInTheLine()
    {
        var first = await _db.Signups.SignUpAsync(EventId, 1, "mage", "fire");
        var second = await _db.Signups.SignUpAsync(EventId, 2, "priest", "holy");
        var third = await _db.Signups.SignUpAsync(EventId, 3, "rogue", "subtlety");

        Assert.Equal(new int?[] { 1, 2, 3 }, new[] { first, second, third });
    }

    [Fact]
    public async Task SignUp_AlreadySignedUpWithANewSpec_KeepsThePlaceAndUpdatesTheSpec()
    {
        await _db.Signups.SignUpAsync(EventId, 1, "mage", "fire");
        await _db.Signups.SignUpAsync(EventId, 2, "priest", "holy");

        var order = await _db.Signups.SignUpAsync(EventId, 1, "mage", "frost");

        Assert.Equal(1, order);
        var stored = await StoredAsync(1);
        Assert.Equal(("frost", 1), (stored.WowSpec, stored.SignupOrder));
    }

    [Fact]
    public async Task SignUp_NewUserWithoutASpec_HasNoPlaceInTheLine()
    {
        var order = await _db.Signups.SignUpAsync(EventId, 1);

        Assert.Null(order);
        var stored = await StoredAsync(1);
        Assert.Equal((SignupStatus.SignedUp, null), (stored.Status, stored.SignupOrder));
    }

    [Fact]
    public async Task SignUp_ComingBackAfterSigningOff_JoinsTheEndOfTheLineKeepingTheEarlierSpec()
    {
        await _db.Signups.SignUpAsync(EventId, 1, "mage", "fire");
        await _db.Signups.SignUpAsync(EventId, 2, "priest", "holy");
        await _db.Signups.SignOffAsync(EventId, 1, compactOrder: true);

        // A plain re-signup, like the checkmark reaction, carries no class
        var order = await _db.Signups.SignUpAsync(EventId, 1);

        Assert.Equal(2, order);
        var stored = await StoredAsync(1);
        Assert.Equal(("fire", SignupStatus.SignedUp, null), (stored.WowSpec, stored.Status, stored.SignedOffAt));
    }

    [Fact]
    public async Task SignOff_WithCompaction_ClosesTheGapSoOrdersStayConsecutive()
    {
        await SignUpMagesAsync(4);

        var signedOff = await _db.Signups.SignOffAsync(EventId, 2, compactOrder: true);

        Assert.True(signedOff);
        Assert.Equal(SignupStatus.SignedOff, (await StoredAsync(2)).Status);
        Assert.Equal(new int?[] { 1, 2, 3 }, await SignedUpOrdersAsync());
        Assert.Equal((1, 2, 3), ((await StoredAsync(1)).SignupOrder, (await StoredAsync(3)).SignupOrder, (await StoredAsync(4)).SignupOrder));
    }

    [Fact]
    public async Task SignOff_WithoutCompaction_LeavesTheRemainingNumbersAlone()
    {
        await SignUpMagesAsync(3);

        await _db.Signups.SignOffAsync(EventId, 1, compactOrder: false);

        Assert.Equal(new int?[] { 2, 3 }, await SignedUpOrdersAsync());
    }

    [Fact]
    public async Task SignOff_SignedUpUser_RecordsWhenTheyLeft()
    {
        await _db.Signups.SignUpAsync(EventId, 1);

        await _db.Signups.SignOffAsync(EventId, 1, compactOrder: false);

        var stored = await StoredAsync(1);
        Assert.Equal(SignupStatus.SignedOff, stored.Status);
        Assert.NotNull(stored.SignedOffAt);
    }

    [Fact]
    public async Task SignOff_UserWhoNeverSignedUp_ReturnsFalse()
    {
        var signedOff = await _db.Signups.SignOffAsync(EventId, 999, compactOrder: false);

        Assert.False(signedOff);
    }

    [Fact]
    public async Task MarkUnsure_NewUser_CreatesAnUnsureSignup()
    {
        await _db.Signups.MarkUnsureAsync(EventId, 1, compactOrder: false);

        Assert.Equal(SignupStatus.Unsure, (await StoredAsync(1)).Status);
    }

    [Fact]
    public async Task MarkUnsure_SignedUpUserWithCompaction_BecomesUnsureAndTheOthersCloseTheGap()
    {
        await SignUpMagesAsync(3);

        await _db.Signups.MarkUnsureAsync(EventId, 1, compactOrder: true);

        Assert.Equal(SignupStatus.Unsure, (await StoredAsync(1)).Status);
        Assert.Equal(new int?[] { 1, 2 }, await SignedUpOrdersAsync());
    }

    [Fact]
    public async Task GetEventSignup_SeveralUsersAndEvents_ReturnsOnlyThatUsersRowForThatEvent()
    {
        await _db.Events.CreateEventAsync(TestEvents.Scheduled(5));
        await _db.Events.CreateEventAsync(TestEvents.Scheduled(6));
        await _db.Signups.SignUpAsync(5, 100);
        await _db.Signups.SignUpAsync(5, 200);
        await _db.Signups.SignUpAsync(6, 100);

        var found = await _db.Signups.GetEventSignupAsync(5, 100);
        var missing = await _db.Signups.GetEventSignupAsync(5, 999);

        Assert.Equal((5UL, 100UL), (found!.EventId, found.UserId));
        Assert.Null(missing);
    }
}
