namespace DiscordBot.Discord.Services;

public record SpecSelectionContext(ulong UserId, ulong MessageId, ulong EventId, string ClassName, ulong GuildId);
