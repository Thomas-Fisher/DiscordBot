namespace DiscordBot.Contracts;

/// <summary>Only the values that are supplied are changed. An <see cref="EchoChannelId"/> of <see cref="NoChannel"/> clears the log channel.</summary>
public sealed record UpdateSettingsRequest
{
    public const ulong NoChannel = 0;

    public ulong? EchoChannelId { get; init; }
    public bool? EchoEnabled { get; init; }
    public string? TimeZone { get; init; }

    /// <summary>Replaces the switched-off audit categories (an empty list switches all back on); null leaves them as they are.</summary>
    public List<string>? DisabledAuditCategories { get; init; }
}
