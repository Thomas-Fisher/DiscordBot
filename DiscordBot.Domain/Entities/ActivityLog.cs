namespace DiscordBot.Domain.Entities;

public class ActivityLog
{
    public ulong LogId { get; set; }
    public DateTime Timestamp { get; set; }
    public ulong? GuildId { get; set; }
    public ulong? UserId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public ulong? EntityId { get; set; }
    public string? Source { get; set; }
    public string? Changes { get; set; }
}
