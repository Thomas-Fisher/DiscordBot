namespace DiscordBot.Application.Models;

public sealed record GuildMember(ulong Id, string DisplayName, string Username, string? AvatarUrl);
