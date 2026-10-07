using DiscordBot.Application.Models;

namespace DiscordBot.Application.Interfaces;

public interface IActivityLogger
{
    /// <summary>Records the entry and lets the guild's log channel know. Never throws: failing to log must not fail what is being logged.</summary>
    Task LogAsync(ActivityEntry entry);
}
