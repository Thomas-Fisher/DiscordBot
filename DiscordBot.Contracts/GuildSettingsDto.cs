namespace DiscordBot.Contracts;

/// <summary>Logs are always stored; the "echo" settings only control the copy posted into a channel.</summary>
public sealed record GuildSettingsDto
{
    public ulong GuildId { get; init; }

    public ulong? EchoChannelId { get; init; }

    public bool EchoEnabled { get; init; }

    /// <summary>True when an echo will actually be posted: a channel is set and echoing is on.</summary>
    public bool EchoActive { get; init; }

    /// <summary>Activity categories that are not recorded (Members, Channels, ...). Empty when everything is.</summary>
    public List<string> DisabledAuditCategories { get; init; } = new();

    /// <summary>An IANA name such as "Europe/London". "UTC" until one is chosen.</summary>
    public string TimeZone { get; init; } = "UTC";
}
