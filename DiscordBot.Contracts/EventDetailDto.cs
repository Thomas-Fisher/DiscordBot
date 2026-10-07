namespace DiscordBot.Contracts;

public sealed record EventDetailDto : EventDtoBase
{
    public ulong GuildId { get; init; }
    public ulong ChannelId { get; init; }
    public ulong MessageId { get; init; }
    public List<SignupDto> Signups { get; init; } = new();
    public ulong? ParentRecurringEventId { get; init; }

    /// <summary>Minutes before the start that people who have not responded are pinged. Null when there is no reminder.</summary>
    public int? ReminderMinutes { get; init; }
}
