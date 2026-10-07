namespace DiscordBot.Application.Options;

public class EventReminderOptions : IValidatableOptions
{
    public const string SectionName = "EventReminders";

    public bool Enabled { get; set; } = true;
    public int CheckIntervalSeconds { get; set; } = 60;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (CheckIntervalSeconds <= 0)
        {
            errors.Add("EventReminders:CheckIntervalSeconds must be greater than 0");
        }


        return errors;
    }
}
