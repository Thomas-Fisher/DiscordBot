namespace DiscordBot.Application;

public static class SettingKeys
{
    public const string EchoChannel = "echo_channel";

    /// <summary>Whether logs are echoed into that channel. Logs are always stored in the database regardless.</summary>
    public const string EchoEnabled = "echo_enabled";

    /// <summary>Activity categories the guild does not want recorded (JSON list of names). Everything is recorded until some are listed.</summary>
    public const string DisabledAuditCategories = "audit_disabled";

    /// <summary>The guild's time zone (IANA name). Times are stored in UTC; this is how people enter and read them.</summary>
    public const string TimeZone = "time_zone";
}
