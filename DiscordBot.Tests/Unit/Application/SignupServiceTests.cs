using DiscordBot.Application.Errors;
using DiscordBot.Application.Models;
using DiscordBot.Application.Services;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using DiscordBot.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Tests.Unit.Application;

public class SignupServiceTests : IAsyncLifetime
{
    private const ulong User = 42;

    private static readonly ActivityContext Discord = ActivityContext.Discord(User);
    private static readonly ActivityContext Admin = ActivityContext.Api(7);

    private readonly TestDb _db = new();
    private readonly RecordingActivityLogger _activity = new();
    private readonly RecordingMessageUpdater _updater = new();
    private readonly FixedTimeProvider _time = new();
    private readonly SignupService _service;

    public SignupServiceTests()
    {
        _service = new SignupService(_db.Signups, _activity, _updater, _time);
    }

    // Signups belong to an event, and the database checks that it exists: the two the tests use are stored up front
    public async Task InitializeAsync()
    {
        await _db.Events.CreateEventAsync(WowEvent());
        await _db.Events.CreateEventAsync(GenericEvent());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Event WowEvent() => TestEvents.Wow();

    private static Event GenericEvent() => TestEvents.Scheduled(2);

    private async Task<List<EventSignup>> StoredSignupsAsync(ulong eventId)
    {
        using var context = _db.Open();
        return await context.EventSignups.AsNoTracking().Where(s => s.EventId == eventId).OrderBy(s => s.SignedUpAt).ThenBy(s => s.UserId).ToListAsync();
    }

    private async Task<EventSignup?> StoredSignupAsync(ulong eventId, ulong userId)
    {
        return (await StoredSignupsAsync(eventId)).SingleOrDefault(s => s.UserId == userId);
    }

    private async Task SignUpMagesAsync(Event evt, params ulong[] users)
    {
        foreach (var user in users)
        {
            await _service.SignUpAsync(evt, user, "mage", "fire", ActivityContext.Discord(user));
        }
    }

    public static IEnumerable<object[]> EventsThatAreNotOpen()
    {
        yield return new object[] { TestEvents.Wow().WithStatus(EventStatus.Cancelled) };
        yield return new object[] { TestEvents.Wow().WithStatus(EventStatus.Completed) };
        yield return new object[] { TestEvents.Wow().Deleted() };
    }

    [Fact]
    public async Task SetRoster_PeopleSignedUpToAWowEvent_PutsThemInGroupsLogsItAndRefreshesTheMessage()
    {
        var evt = WowEvent();
        await SignUpMagesAsync(evt, 1, 2, 3);

        var result = await _service.SetRosterAsync(evt, new Dictionary<ulong, int> { [1] = 1, [2] = 1, [3] = 2 }, Admin);

        Assert.True(result.Success);
        Assert.Equal(new int?[] { 1, 1, 2 }, (await StoredSignupsAsync(evt.EventId)).Select(s => s.GroupNumber));
        Assert.Contains(_activity.Entries, e => e.Action == "RosterUpdated" && e.EntityId == evt.EventId && e.UserId == 7);
        Assert.Contains(evt.MessageId!.Value, _updater.Refreshed);
    }

    [Fact]
    public async Task SetRoster_GenericEvent_IsRefusedBecauseGroupsAreOnlyForWowEvents()
    {
        var result = await _service.SetRosterAsync(GenericEvent(), new Dictionary<ulong, int>(), Admin);

        Assert.Equal(SignupErrors.RosterNeedsWowEvent, result.Error);
    }

    [Theory]
    [MemberData(nameof(EventsThatAreNotOpen))]
    public async Task SetRoster_EventThatIsNotOpen_IsRefused(Event closed)
    {
        var result = await _service.SetRosterAsync(closed, new Dictionary<ulong, int>(), Admin);

        Assert.Equal(SignupErrors.EventNotOpen, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(-1)]
    public async Task SetRoster_GroupNumberOutsideOneToEight_IsRefusedAndNothingChanges(int group)
    {
        var evt = WowEvent();
        await SignUpMagesAsync(evt, 1);

        var result = await _service.SetRosterAsync(evt, new Dictionary<ulong, int> { [1] = group }, Admin);

        Assert.Equal(SignupErrors.RosterBadGroup, result.Error);
        Assert.Null((await StoredSignupAsync(evt.EventId, 1))!.GroupNumber);
    }

    [Fact]
    public async Task SetRoster_SixPeopleInOneGroup_IsRefusedAndNothingChanges()
    {
        var evt = WowEvent();
        await SignUpMagesAsync(evt, 1, 2, 3, 4, 5, 6);

        var result = await _service.SetRosterAsync(evt, Enumerable.Range(1, 6).ToDictionary(user => (ulong)user, _ => 1), Admin);

        Assert.Equal(SignupErrors.RosterGroupFull, result.Error);
        Assert.All(await StoredSignupsAsync(evt.EventId), s => Assert.Null(s.GroupNumber));
    }

    [Fact]
    public async Task SetRoster_FiveInAGroupAndAnotherGroup_IsAccepted()
    {
        var evt = WowEvent();
        await SignUpMagesAsync(evt, 1, 2, 3, 4, 5, 6);

        var result = await _service.SetRosterAsync(evt, new Dictionary<ulong, int> { [1] = 1, [2] = 1, [3] = 1, [4] = 1, [5] = 1, [6] = 2 }, Admin);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task SetRoster_PersonWhoAnsweredUnsure_IsAccepted()
    {
        var evt = WowEvent();
        await SignUpMagesAsync(evt, 1);
        await _service.MarkUnsureAsync(evt, 2, Discord);

        var result = await _service.SetRosterAsync(evt, new Dictionary<ulong, int> { [1] = 1, [2] = 1 }, Admin);

        Assert.True(result.Success);
        Assert.Equal(1, (await StoredSignupAsync(evt.EventId, 2))!.GroupNumber);
    }

    [Fact]
    public async Task SetRoster_SomeoneWhoIsNotSignedUp_IsRefused()
    {
        var evt = WowEvent();
        await SignUpMagesAsync(evt, 1);

        var result = await _service.SetRosterAsync(evt, new Dictionary<ulong, int> { [1] = 1, [2] = 1 }, Admin);

        Assert.Equal(SignupErrors.RosterNotSignedUp, result.Error);
        Assert.Null((await StoredSignupAsync(evt.EventId, 1))!.GroupNumber);
    }

    [Fact]
    public async Task SignUp_GenericEvent_RecordsTheSignupLogsItAndRefreshesTheMessage()
    {
        var evt = GenericEvent();

        var result = await _service.SignUpAsync(evt, User, null, null, Discord);

        Assert.True(result.Success);
        Assert.Null(result.Value);
        Assert.Equal(SignupStatus.SignedUp, (await StoredSignupAsync(evt.EventId, User))!.Status);
        Assert.Single(_activity.Entries, e => e.Action == "SignedUp" && e.UserId == User && e.Source == "Discord");
        Assert.Contains(evt.MessageId!.Value, _updater.Refreshed);
    }

    [Fact]
    public async Task SignUp_WowEventWithMessySpelling_StoresTheNormalisedClassAndSpec()
    {
        var evt = WowEvent();

        await _service.SignUpAsync(evt, User, "  MAGE ", "Fire", Discord);

        var stored = await StoredSignupAsync(evt.EventId, User);
        Assert.Equal(("mage", "fire"), (stored!.WowClass, stored.WowSpec));
    }

    [Theory]
    [MemberData(nameof(EventsThatAreNotOpen))]
    public async Task SignUp_EventThatIsCancelledCompletedOrDeleted_IsRejectedAndNothingIsStored(Event evt)
    {
        var result = await _service.SignUpAsync(evt, User, null, null, Discord);

        Assert.Equal(SignupErrors.EventNotOpen, result.Error);
        Assert.Empty(await StoredSignupsAsync(evt.EventId));
    }

    [Fact]
    public async Task SignUp_EventThatHasStartedButNotEnded_IsAccepted()
    {
        var evt = GenericEvent();
        evt.DurationMinutes = 120;
        _time.Now = new DateTimeOffset(evt.ScheduledTime.AddMinutes(90), TimeSpan.Zero);

        var result = await _service.SignUpAsync(evt, User, null, null, Discord);

        Assert.True(result.Success);
    }

    [Theory]
    [InlineData(null, 0)]    // no duration: over as soon as it starts
    [InlineData(120, 120)]
    [InlineData(120, 500)]
    public async Task SignUp_EventThatHasEnded_IsRejectedAndNothingIsStored(int? duration, int minutesAfterStart)
    {
        var evt = GenericEvent();
        evt.DurationMinutes = duration;
        _time.Now = new DateTimeOffset(evt.ScheduledTime.AddMinutes(minutesAfterStart), TimeSpan.Zero);

        var result = await _service.SignUpAsync(evt, User, null, null, Discord);

        Assert.Equal(SignupErrors.EventNotOpen, result.Error);
        Assert.Empty(await StoredSignupsAsync(evt.EventId));
    }

    [Theory]
    [InlineData("mage", null, "signup.class_and_spec_together")]
    [InlineData(null, "fire", "signup.class_and_spec_together")]
    [InlineData("mage", "arms", "signup.invalid_class_or_spec")]          // a warrior spec
    [InlineData("evoker", "devastation", "signup.invalid_class_or_spec")] // not in this game version
    public async Task SignUp_BadClassOrSpecForAWowEvent_IsRejectedAndNothingIsStored(string? wowClass, string? wowSpec, string expectedCode)
    {
        var evt = WowEvent();

        var result = await _service.SignUpAsync(evt, User, wowClass, wowSpec, Discord);

        Assert.Equal(expectedCode, result.Error!.Code);
        Assert.Empty(await StoredSignupsAsync(evt.EventId));
    }

    [Fact]
    public async Task SignUp_ClassOnAGenericEvent_IsRejectedAndNothingIsStored()
    {
        var evt = GenericEvent();

        var result = await _service.SignUpAsync(evt, User, "mage", "fire", Discord);

        Assert.Equal(SignupErrors.InvalidClassOrSpec, result.Error);
        Assert.Empty(await StoredSignupsAsync(evt.EventId));
    }

    [Fact]
    public async Task SignUp_AlreadySignedUpWithoutPickingANewSpec_IsRejectedAndTheSpecIsUnchanged()
    {
        var evt = WowEvent();
        await SignUpMagesAsync(evt, User);

        var result = await _service.SignUpAsync(evt, User, null, null, Discord);

        Assert.Equal(SignupErrors.AlreadySignedUp, result.Error);
        Assert.Equal("fire", (await StoredSignupAsync(evt.EventId, User))!.WowSpec);
    }

    [Fact]
    public async Task SignUp_AlreadySignedUpButPickingANewSpec_ChangesTheSpecAndKeepsThePlace()
    {
        var evt = WowEvent();
        await SignUpMagesAsync(evt, 1, User);

        var result = await _service.SignUpAsync(evt, User, "mage", "frost", Discord);

        Assert.True(result.Success);
        var stored = await StoredSignupAsync(evt.EventId, User);
        Assert.Equal(("frost", 2), (stored!.WowSpec, stored.SignupOrder));
    }

    [Fact]
    public async Task SignUp_ByAnAdminThroughTheApi_IsLoggedAgainstTheAdminAndTheApi()
    {
        var evt = GenericEvent();

        await _service.SignUpAsync(evt, User, null, null, Admin);

        Assert.Single(_activity.Entries, e => e.Action == "SignedUp" && e.UserId == 7 && e.Source == "Api");
    }

    [Fact]
    public async Task SignOff_WowEventWithSeveralSignups_ClosesTheGapInTheOrder()
    {
        var evt = WowEvent();
        await SignUpMagesAsync(evt, 1, 2, 3);

        var result = await _service.SignOffAsync(evt, 1, ActivityContext.Discord(1));

        Assert.True(result.Success);
        Assert.Equal(SignupStatus.SignedOff, (await StoredSignupAsync(evt.EventId, 1))!.Status);
        Assert.Equal(1, (await StoredSignupAsync(evt.EventId, 2))!.SignupOrder);
        Assert.Equal(2, (await StoredSignupAsync(evt.EventId, 3))!.SignupOrder);
        Assert.Contains(evt.MessageId!.Value, _updater.Refreshed);
    }

    [Fact]
    public async Task SignOff_SignedUpUser_IsRecordedAsSignedOff()
    {
        var evt = GenericEvent();
        await _service.SignUpAsync(evt, User, null, null, Discord);

        var result = await _service.SignOffAsync(evt, User, Discord);

        Assert.True(result.Success);
        Assert.Equal(SignupStatus.SignedOff, (await StoredSignupAsync(evt.EventId, User))!.Status);
    }

    [Fact]
    public async Task SignOff_AlreadySignedOff_ReportsNotSignedUp()
    {
        var evt = GenericEvent();
        await _service.SignUpAsync(evt, User, null, null, Discord);
        await _service.SignOffAsync(evt, User, Discord);
        _updater.Refreshed.Clear();

        var result = await _service.SignOffAsync(evt, User, Discord);

        Assert.Equal(SignupErrors.NotSignedUp, result.Error);
        Assert.Empty(_updater.Refreshed);
    }

    [Fact]
    public async Task SignOff_NeverSignedUp_ReportsNotSignedUp()
    {
        var evt = WowEvent();

        var result = await _service.SignOffAsync(evt, User, Discord);

        Assert.Equal(SignupErrors.NotSignedUp, result.Error);
        Assert.Empty(await StoredSignupsAsync(evt.EventId));
    }

    [Fact]
    public async Task SignOff_EventAlreadyCancelled_IsStillAllowedSoAnAdminCanTidyUp()
    {
        var evt = WowEvent();
        await SignUpMagesAsync(evt, User);
        evt.Status = EventStatus.Cancelled;

        var result = await _service.SignOffAsync(evt, User, Admin);

        Assert.True(result.Success);
        Assert.Equal(SignupStatus.SignedOff, (await StoredSignupAsync(evt.EventId, User))!.Status);
    }

    [Fact]
    public async Task MarkUnsure_AlreadyUnsure_ReportsNothingToChange()
    {
        var evt = GenericEvent();
        await _service.SignUpAsync(evt, User, null, null, Discord);
        await _service.MarkUnsureAsync(evt, User, Discord);

        var result = await _service.MarkUnsureAsync(evt, User, Discord);

        Assert.Equal(SignupErrors.NothingToChange, result.Error);
    }

    [Theory]
    [MemberData(nameof(EventsThatAreNotOpen))]
    public async Task MarkUnsure_EventThatIsNotOpen_IsRejectedAndNothingIsStored(Event evt)
    {
        var result = await _service.MarkUnsureAsync(evt, User, Discord);

        Assert.Equal(SignupErrors.EventNotOpen, result.Error);
        Assert.Empty(await StoredSignupsAsync(evt.EventId));
    }

    [Theory]
    [InlineData("signedoff", SignupStatus.SignedOff)]
    [InlineData("SignedOff", SignupStatus.SignedOff)]
    [InlineData("Unsure", SignupStatus.Unsure)]
    public async Task Update_StatusInAnyCase_IsAppliedToTheSignup(string status, SignupStatus expected)
    {
        var evt = WowEvent();
        await SignUpMagesAsync(evt, User);

        var result = await _service.UpdateAsync(evt, User, new SignupUpdate(Status: status), Admin);

        Assert.True(result.Success);
        Assert.Equal(expected, (await StoredSignupAsync(evt.EventId, User))!.Status);
    }

    [Theory]
    [InlineData("Banana")]
    [InlineData("42")]
    public async Task Update_UnknownStatus_IsRejectedAndTheSignupIsUnchanged(string status)
    {
        var evt = WowEvent();
        await SignUpMagesAsync(evt, User);

        var result = await _service.UpdateAsync(evt, User, new SignupUpdate(Status: status), Admin);

        Assert.Equal(SignupErrors.InvalidStatus, result.Error);
        Assert.Equal(SignupStatus.SignedUp, (await StoredSignupAsync(evt.EventId, User))!.Status);
    }

    [Fact]
    public async Task Update_SomeoneWhoLeftBackToSignedUp_SignsThemUpAgain()
    {
        var evt = WowEvent();
        await SignUpMagesAsync(evt, User);
        await _service.SignOffAsync(evt, User, Discord);

        var result = await _service.UpdateAsync(evt, User, new SignupUpdate(Status: "SignedUp"), Admin);

        Assert.True(result.Success);
        Assert.Equal(SignupStatus.SignedUp, (await StoredSignupAsync(evt.EventId, User))!.Status);
    }

    [Fact]
    public async Task Update_OnlyTheClassAndSpec_ChangesTheSpecOfSomeoneSignedUp()
    {
        var evt = WowEvent();
        await SignUpMagesAsync(evt, User);

        var result = await _service.UpdateAsync(evt, User, new SignupUpdate(WowClass: "priest", WowSpec: "shadow"), Admin);

        Assert.True(result.Success);
        var stored = await StoredSignupAsync(evt.EventId, User);
        Assert.Equal(("priest", "shadow"), (stored!.WowClass, stored.WowSpec));
    }

    [Fact]
    public async Task Update_SettingASpecWhileSigningOff_IsRejectedAndTheSignupIsUnchanged()
    {
        var evt = WowEvent();
        await SignUpMagesAsync(evt, User);

        var result = await _service.UpdateAsync(evt, User, new SignupUpdate("SignedOff", "priest", "shadow"), Admin);

        Assert.Equal(SignupErrors.ClassSpecRequiresSignedUp, result.Error);
        Assert.Equal(SignupStatus.SignedUp, (await StoredSignupAsync(evt.EventId, User))!.Status);
    }

    [Fact]
    public async Task Update_SignedUpUserToSignedUp_ReportsNothingToChange()
    {
        var evt = GenericEvent();
        await _service.SignUpAsync(evt, User, null, null, Discord);

        var result = await _service.UpdateAsync(evt, User, new SignupUpdate(Status: "SignedUp"), Admin);

        Assert.Equal(SignupErrors.NothingToChange, result.Error);
    }

    public static IEnumerable<object[]> IncompleteUpdates()
    {
        yield return new object[] { new SignupUpdate(), "signup.nothing_to_change" };
        yield return new object[] { new SignupUpdate(WowClass: "mage"), "signup.class_and_spec_together" };
    }

    [Theory]
    [MemberData(nameof(IncompleteUpdates))]
    public async Task Update_NothingOrHalfAClassSpecPair_IsRejected(SignupUpdate update, string expectedCode)
    {
        var evt = WowEvent();
        await SignUpMagesAsync(evt, User);

        var result = await _service.UpdateAsync(evt, User, update, Admin);

        Assert.Equal(expectedCode, result.Error!.Code);
    }
}
