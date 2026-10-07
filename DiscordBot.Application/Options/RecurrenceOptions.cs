namespace DiscordBot.Application.Options;

public class RecurrenceOptions : IValidatableOptions
{
    public const string SectionName = "Recurrence";

    /// <summary>How often the worker looks for recurring events whose time has come, and for events that still need announcing.</summary>
    public int CheckIntervalSeconds { get; set; } = 60;

    public IReadOnlyList<string> Validate()
    {
        return CheckIntervalSeconds > 0 ? [] : ["Recurrence:CheckIntervalSeconds must be greater than 0"];
    }
}
