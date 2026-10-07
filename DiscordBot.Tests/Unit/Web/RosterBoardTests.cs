using Bunit;
using DiscordBot.Contracts;
using DiscordBot.Web.Components;
using Microsoft.AspNetCore.Components;

namespace DiscordBot.Tests.Unit.Web;

public class RosterBoardTests : BunitContext
{
    private List<RosterAssignment>? _saved;

    private static SignupDetailsDto Person(ulong id, string wowClass = "mage", string wowSpec = "fire", int? group = null, string status = "SignedUp") => new()
    {
        SignupId = id,
        UserId = id,
        DisplayName = $"Player{id}",
        Status = status,
        WowClass = wowClass,
        WowSpec = wowSpec,
        SignupOrder = (int)id,
        Group = group,
        SignedUpAt = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc).AddMinutes((int)id)
    };

    private IRenderedComponent<RosterBoard> RenderBoard(params SignupDetailsDto[] signups)
    {
        return Render<RosterBoard>(parameters => parameters
            .Add(p => p.Signups, signups)
            .Add(p => p.OnSave, EventCallback.Factory.Create<List<RosterAssignment>>(this, assignments => _saved = assignments)));
    }

    private static List<SignupDetailsDto> Many(int count) =>
        Enumerable.Range(1, count).Select(i => Person((ulong)i)).ToList();

    private static void Drag(IRenderedComponent<RosterBoard> board, ulong userId, string boxId)
    {
        board.Find($"#roster-card-{userId}").DragStart();
        board.Find(boxId).Drop();
    }

    private static string[] NamesIn(IRenderedComponent<RosterBoard> board, string boxId) =>
        board.FindAll($"{boxId} .signup-name").Select(e => e.TextContent.Trim()).ToArray();

    [Fact]
    public void Render_PeopleSignedUpWithSomeInGroups_ShowsEachGroupAndTheStandbyAndTheRosterTotals()
    {
        var board = RenderBoard(Person(1, "warrior", "protection", group: 1), Person(2, "priest", "holy", group: 1), Person(3, "mage", "fire", group: 2), Person(4, "rogue", "assassination"));

        Assert.Equal(new[] { "Player1", "Player2" }, NamesIn(board, "#roster-box-1"));
        Assert.Equal(new[] { "Player3" }, NamesIn(board, "#roster-box-2"));
        Assert.Equal(new[] { "Player4" }, NamesIn(board, "#roster-box-standby"));
        var summary = board.Find("#roster-summary").TextContent;
        Assert.Contains("Tanks 1", summary);
        Assert.Contains("Healers 1", summary);
        Assert.Contains("Melee 0", summary);
        Assert.Contains("Ranged 1", summary);
    }

    [Fact]
    public void Render_PeopleWhoAreNotSignedUp_AreNotOnTheBoard()
    {
        var board = RenderBoard(Person(1), Person(2, status: "Unsure"), Person(3, status: "SignedOff"));

        Assert.Equal(new[] { "Player1" }, NamesIn(board, "#roster-box-standby"));
    }

    [Fact]
    public void Render_UnsurePeople_AreInTheirOwnBoxAndCanBeDraggedIntoAGroup()
    {
        var board = RenderBoard(Person(1, group: 1), Person(2, status: "Unsure"));
        Assert.Equal(new[] { "Player2 🤷" }, NamesIn(board, "#roster-box-unsure"));

        Drag(board, 2, "#roster-box-1");

        Assert.Equal(new[] { "Player1", "Player2 🤷" }, NamesIn(board, "#roster-box-1"));
        Assert.Empty(board.FindAll("#roster-box-unsure"));
        board.Find("#roster-save").Click();
        Assert.Equal(new[] { (1UL, 1), (2UL, 1) }, _saved!.Select(a => (a.UserId, a.Group)));
    }

    [Fact]
    public void Render_UnsurePersonAlreadyInAGroup_ShowsThereAndCountsInTheRosterTotals()
    {
        var board = RenderBoard(Person(1, "priest", "holy", group: 1, status: "Unsure"));

        Assert.Equal(new[] { "Player1 🤷" }, NamesIn(board, "#roster-box-1"));
        Assert.Contains("Healers 1", board.Find("#roster-summary").TextContent);
    }

    [Fact]
    public void AutoFill_PeopleWhoAreUnsure_AreNotPutInGroups()
    {
        var board = RenderBoard(Person(1), Person(2, status: "Unsure"), Person(3));

        board.Find("#roster-autofill-count").Change("10");
        board.Find("#roster-autofill").Click();

        Assert.Equal(new[] { "Player1", "Player3" }, NamesIn(board, "#roster-box-1"));
        Assert.Equal(new[] { "Player2 🤷" }, NamesIn(board, "#roster-box-unsure"));
    }

    [Fact]
    public void Drop_CardOnAGroup_MovesThePersonAndLetsTheRosterBeSaved()
    {
        var board = RenderBoard(Person(1, group: 1), Person(2));
        Assert.True(board.Find("#roster-save").HasAttribute("disabled"));

        Drag(board, 2, "#roster-box-1");

        Assert.Equal(new[] { "Player1", "Player2" }, NamesIn(board, "#roster-box-1"));
        Assert.Empty(NamesIn(board, "#roster-box-standby"));
        Assert.False(board.Find("#roster-save").HasAttribute("disabled"));

        board.Find("#roster-save").Click();

        Assert.Equal(new[] { (1UL, 1), (2UL, 1) }, _saved!.Select(a => (a.UserId, a.Group)));
    }

    [Fact]
    public void Drop_CardOnAFullGroup_LeavesThePersonWhereTheyWere()
    {
        var board = RenderBoard(Many(6).Select((p, i) => i < 5 ? Person(p.UserId, group: 1) : p).ToArray());

        Drag(board, 6, "#roster-box-1");

        Assert.Equal(5, NamesIn(board, "#roster-box-1").Length);
        Assert.Equal(new[] { "Player6" }, NamesIn(board, "#roster-box-standby"));
    }

    [Fact]
    public void Drop_CardOnTheNewGroupBox_StartsTheNextGroup()
    {
        var board = RenderBoard(Person(1, group: 1), Person(2));

        Drag(board, 2, "#roster-box-new");

        Assert.Equal(new[] { "Player2" }, NamesIn(board, "#roster-box-2"));
    }

    [Fact]
    public void Drop_CardBackOnStandby_TakesThePersonOutOfTheirGroup()
    {
        var board = RenderBoard(Person(1, group: 1));

        Drag(board, 1, "#roster-box-standby");

        Assert.Equal(new[] { "Player1" }, NamesIn(board, "#roster-box-standby"));
        Assert.DoesNotContain(board.FindAll(".roster-box"), box => box.Id == "roster-box-1");
    }

    [Fact]
    public void Select_DropDownOnACard_MovesThePersonWithoutDragging()
    {
        var board = RenderBoard(Person(1), Person(2, group: 1));

        board.Find("#roster-card-1 select").Change("1");

        Assert.Equal(new[] { "Player1", "Player2" }, NamesIn(board, "#roster-box-1").Order());
    }

    [Fact]
    public void Select_FullGroup_CannotBeChosenFromTheDropDown()
    {
        var board = RenderBoard(Many(6).Select((p, i) => i < 5 ? Person(p.UserId, group: 1) : p).ToArray());

        var option = board.Find("#roster-card-6 select option[value='1']");

        Assert.True(option.HasAttribute("disabled"));
    }

    [Fact]
    public void AutoFill_TwelvePeopleAndTen_PutsTheFirstTenInTwoGroupsOfFiveInSignupOrder()
    {
        var board = RenderBoard(Many(12).ToArray());

        board.Find("#roster-autofill-count").Change("10");
        board.Find("#roster-autofill").Click();

        Assert.Equal(new[] { "Player1", "Player2", "Player3", "Player4", "Player5" }, NamesIn(board, "#roster-box-1"));
        Assert.Equal(new[] { "Player6", "Player7", "Player8", "Player9", "Player10" }, NamesIn(board, "#roster-box-2"));
        Assert.Equal(new[] { "Player11", "Player12" }, NamesIn(board, "#roster-box-standby"));
    }

    [Fact]
    public void Clear_RosterWithGroups_PutsEveryoneBackOnStandby()
    {
        var board = RenderBoard(Person(1, group: 1), Person(2, group: 2));

        board.Find("#roster-clear").Click();

        Assert.Equal(new[] { "Player1", "Player2" }, NamesIn(board, "#roster-box-standby"));
    }

    [Fact]
    public void Save_GroupsWithAGapBetweenThem_AreSentNumberedOneTwoThree()
    {
        var board = RenderBoard(Person(1), Person(2), Person(3));
        board.Find("#roster-card-1 select").Change("1");
        board.Find("#roster-card-2 select").Change("3");
        board.Find("#roster-card-3 select").Change("4");

        board.Find("#roster-save").Click();

        Assert.Equal(new[] { (1UL, 1), (2UL, 2), (3UL, 3) }, _saved!.Select(a => (a.UserId, a.Group)));
    }

    [Fact]
    public void Discard_AfterMovingPeople_RestoresTheSavedRoster()
    {
        var board = RenderBoard(Person(1, group: 1), Person(2));
        Drag(board, 2, "#roster-box-1");

        board.Find("#roster-discard").Click();

        Assert.Equal(new[] { "Player1" }, NamesIn(board, "#roster-box-1"));
        Assert.Equal(new[] { "Player2" }, NamesIn(board, "#roster-box-standby"));
        Assert.True(board.Find("#roster-save").HasAttribute("disabled"));
    }

    [Fact]
    public void Render_NewSignupListFromTheServer_ReplacesWhateverWasBeingArranged()
    {
        var board = RenderBoard(Person(1), Person(2));
        Drag(board, 1, "#roster-box-new");

        board.Render(parameters => parameters.Add(p => p.Signups, new[] { Person(1, group: 2), Person(2, group: 1) }));

        Assert.Equal(new[] { "Player2" }, NamesIn(board, "#roster-box-1"));
        Assert.Equal(new[] { "Player1" }, NamesIn(board, "#roster-box-2"));
        Assert.True(board.Find("#roster-save").HasAttribute("disabled"));
    }
}
