using DiscordBot.Application.Errors;
using DiscordBot.Domain.Common;

namespace DiscordBot.Tests.Unit.Application;

public class ErrorCatalogueTests
{
    public static IEnumerable<object[]> EveryEventValidationError() => Enum.GetValues<EventValidationError>().Select(e => new object[] { e });

    [Theory]
    [MemberData(nameof(EveryEventValidationError))]
    public void From_EveryEventValidationError_HasItsOwnCodeAndText(EventValidationError rule)
    {
        // A new rule added without wording throws instead of silently saying something generic
        var error = EventErrors.From(rule);

        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.StartsWith("event.", error.Code);
    }

    [Fact]
    public void Catalogues_EveryErrorHasAUniqueCode()
    {
        var errors = new[] { typeof(EventErrors), typeof(SignupErrors) }
            .SelectMany(t => t.GetFields().Where(f => f.FieldType == typeof(Error)))
            .Select(f => (Error)f.GetValue(null)!)
            .ToList();

        Assert.NotEmpty(errors);
        Assert.Equal(errors.Count, errors.Select(e => e.Code).Distinct().Count());
    }

    [Fact]
    public void From_UndefinedRule_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EventErrors.From((EventValidationError)999));
    }
}
