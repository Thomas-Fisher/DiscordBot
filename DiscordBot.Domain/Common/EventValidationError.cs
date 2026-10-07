namespace DiscordBot.Domain.Common;

public enum EventValidationError
{
    TitleRequired,
    TitleTooShort,
    TitleTooLong,
    DescriptionTooLong,
    DateMustBeInFuture,
    ReminderOutOfRange,
    DurationOutOfRange,
    TooManyMentionRoles,
    NothingToChange,
    NotChangeable
}
