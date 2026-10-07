using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;

namespace DiscordBot.Tests.Support;

public static class TestEvents
{
    public const ulong GuildId = 10;

    public static Event Scheduled(ulong id = 1, EventType type = EventType.Generic, ulong guildId = GuildId) => new()
    {
        EventId = id,
        GuildId = guildId,
        CreatedBy = 42,
        Title = "Raid night",
        Description = "old description",
        ScheduledTime = DateTime.UtcNow.AddDays(2),
        CreatedAt = DateTime.UtcNow,
        Type = type,
        Status = EventStatus.Scheduled,
        ChannelId = 500 + id,
        MessageId = 1000 + id,
        ReminderMinutesBefore = 30,
        ReminderSent = true
    };

    public static Event Wow(ulong id = 1) => Scheduled(id, EventType.WowClassic);

    public static Event WithStatus(this Event evt, EventStatus status)
    {
        evt.Status = status;
        return evt;
    }

    public static Event WithGuild(this Event evt, ulong guildId)
    {
        evt.GuildId = guildId;
        return evt;
    }

    public static Event Deleted(this Event evt)
    {
        evt.IsDeleted = true;
        return evt;
    }
}
