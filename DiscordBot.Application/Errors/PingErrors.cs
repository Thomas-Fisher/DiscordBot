namespace DiscordBot.Application.Errors;

public static class PingErrors
{
    public const int MaxNoteLength = 300;

    public static readonly Error EventNotOpen = new("ping.event_not_open", "This event is cancelled or finished, so nobody can be pinged about it.");

    public static readonly Error NotAnnounced = new("ping.not_announced", "This event has no announcement message in Discord to reply to.");

    public static readonly Error NeedsRoles = new("ping.needs_roles", "Choose at least one role, so the bot knows whose answers to look for.");

    public static readonly Error UnknownRole = new("ping.unknown_role", "One of those roles is not a role in this server.");

    public static readonly Error GuildUnavailable = new("ping.guild_unavailable", "The bot cannot see this server right now. Try again in a moment.");

    public static readonly Error UnknownTarget = new("ping.unknown_target", "Choose who to ping: Roster, Standby or NoResponse.");

    public static readonly Error NoOneToPing = new("ping.no_one", "There is nobody to ping.");

    public static readonly Error NoteTooLong = new("ping.note_too_long", $"The note must be {MaxNoteLength} characters or fewer.");

    public static readonly Error SendFailed = new("ping.send_failed", "I couldn't post the ping. Check that I can send messages in the event's channel.");
}
