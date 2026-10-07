using DiscordBot.Domain.Common;

namespace DiscordBot.Tests.Unit.Domain;

public class EventRulesTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, EventValidationError.TitleRequired)]
    [InlineData("", EventValidationError.TitleRequired)]
    [InlineData("   ", EventValidationError.TitleRequired)]
    [InlineData("ab", EventValidationError.TitleTooShort)]
    [InlineData(" ab ", EventValidationError.TitleTooShort)]
    public void ValidateTitle_MissingOrTooShort_IsRejected(string? title, EventValidationError expected)
    {
        var error = EventRules.ValidateTitle(title, out _);

        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData(EventRules.MinTitleLength)]
    [InlineData(EventRules.MaxTitleLength)]
    public void ValidateTitle_AtTheLengthLimits_IsAccepted(int length)
    {
        var title = new string('a', length);

        var error = EventRules.ValidateTitle(title, out _);

        Assert.Null(error);
    }

    [Fact]
    public void ValidateTitle_OneOverTheMaximum_IsTooLong()
    {
        var title = new string('a', EventRules.MaxTitleLength + 1);

        var error = EventRules.ValidateTitle(title, out _);

        Assert.Equal(EventValidationError.TitleTooLong, error);
    }

    [Fact]
    public void ValidateTitle_WithSurroundingSpaces_ReturnsTheTrimmedTitle()
    {
        EventRules.ValidateTitle("  padded  ", out var trimmed);

        Assert.Equal("padded", trimmed);
    }

    [Fact]
    public void ValidateDescription_Missing_IsAllowedAndBecomesEmpty()
    {
        var error = EventRules.ValidateDescription(null, out var description);

        Assert.Null(error);
        Assert.Equal("", description);
    }

    [Fact]
    public void ValidateDescription_AtTheMaximumPlusTrailingSpaces_IsAcceptedBecauseItIsTrimmedFirst()
    {
        var description = new string('a', EventRules.MaxDescriptionLength) + "   ";

        var error = EventRules.ValidateDescription(description, out _);

        Assert.Null(error);
    }

    [Fact]
    public void ValidateDescription_OneOverTheMaximum_IsTooLong()
    {
        var description = new string('a', EventRules.MaxDescriptionLength + 1);

        var error = EventRules.ValidateDescription(description, out _);

        Assert.Equal(EventValidationError.DescriptionTooLong, error);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    public void ValidateScheduledTime_MustBeStrictlyAfterNow(int secondsFromNow, bool rejected)
    {
        var scheduled = Now.AddSeconds(secondsFromNow);

        var error = EventRules.ValidateScheduledTime(scheduled, Now);

        Assert.Equal(rejected ? EventValidationError.DateMustBeInFuture : null, error);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(1, false)]
    [InlineData(1440, false)]
    [InlineData(10080, false)]
    [InlineData(0, true)]
    [InlineData(10081, true)]
    [InlineData(-5, true)]
    public void ValidateReminderMinutes_OptionalAndOtherwiseBetweenOneMinuteAndAWeek_IsRejectedOutsideThat(int? minutes, bool rejected)
    {
        var error = EventRules.ValidateReminderMinutes(minutes);

        Assert.Equal(rejected, error != null);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(1, false)]
    [InlineData(1440, false)]
    [InlineData(0, true)]
    [InlineData(1441, true)]
    [InlineData(-5, true)]
    public void ValidateDuration_OptionalAndOtherwiseBetweenOneAndOneThousandFourHundredForty_IsRejectedOutsideThat(int? minutes, bool rejected)
    {
        var error = EventRules.ValidateDuration(minutes);

        Assert.Equal(rejected ? EventValidationError.DurationOutOfRange : null, error);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(25, false)]
    [InlineData(26, true)]
    public void ValidateMentionRoles_MoreThanTwentyFive_IsRejected(int count, bool rejected)
    {
        var roleIds = Enumerable.Range(1, count).Select(i => (ulong)i).ToList();

        var error = EventRules.ValidateMentionRoles(roleIds);

        Assert.Equal(rejected ? EventValidationError.TooManyMentionRoles : null, error);
    }
}
