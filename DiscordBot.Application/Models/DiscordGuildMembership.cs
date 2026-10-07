namespace DiscordBot.Application.Models;

/// <summary>A server a user is in, and the permission bits they hold there (see <see cref="GuildAccessRules"/>).</summary>
public sealed record DiscordGuildMembership(string Id, string Name, string? Icon, bool Owner, long Permissions);
