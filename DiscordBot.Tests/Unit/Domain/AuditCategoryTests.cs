using DiscordBot.Domain.Enums;

namespace DiscordBot.Tests.Unit.Domain;

public class AuditCategoryTests
{
    [Fact]
    public void Of_EveryAction_IsEitherInACategoryOrOneTheBotAlwaysRecords()
    {
        var alwaysRecorded = new[]
        {
            ActivityActionType.Created, ActivityActionType.Updated, ActivityActionType.Deleted, ActivityActionType.Cancelled, ActivityActionType.CancelledRecurring,
            ActivityActionType.CreatedRecurring, ActivityActionType.Completed, ActivityActionType.MarkedUnsure, ActivityActionType.SignedUp, ActivityActionType.SignedOff,
            ActivityActionType.RosterUpdated, ActivityActionType.PingSent
        };

        var uncategorised = Enum.GetValues<ActivityActionType>().Where(a => AuditCategories.Of(a) == null).ToList();

        // A new Discord-activity action has to be given a category, or it cannot be switched off
        Assert.Equal(alwaysRecorded.Order(), uncategorised.Order());
    }
}
