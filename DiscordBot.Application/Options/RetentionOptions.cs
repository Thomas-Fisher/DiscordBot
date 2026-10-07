namespace DiscordBot.Application.Options;

public class RetentionOptions : IValidatableOptions
{
    public const string SectionName = "Retention";

    public bool Enabled { get; set; } = true;

    /// <summary>How long logged message content is kept. 0 keeps it forever.</summary>
    public int MessageLogDays { get; set; } = 30;

    /// <summary>How long activity log rows are kept. 0 keeps them forever.</summary>
    public int ActivityLogDays { get; set; } = 90;

    /// <summary>How long records of handled events (used to skip redelivered events) are kept. 0 keeps them forever.</summary>
    public int ProcessedEventDays { get; set; } = 7;

    public int IntervalHours { get; set; } = 6;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (IntervalHours <= 0)
        {
            errors.Add("Retention:IntervalHours must be greater than 0");
        }

        if (MessageLogDays < 0 || ActivityLogDays < 0 || ProcessedEventDays < 0)
        {
            errors.Add("Retention: the number of days to keep cannot be negative (0 keeps forever)");
        }

        return errors;
    }
}
