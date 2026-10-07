using Discord;
using DiscordBot.Application;
using DiscordBot.Domain.Entities;

namespace DiscordBot.Discord.Services;

public class EventPermissionService
{
    public bool CanCreateEvent(IGuildUser user)
    {
        return IsManager(user);
    }

    public bool CanManageEvent(Event evt, IGuildUser user)
    {
        return evt.CreatedBy == user.Id || IsManager(user);
    }

    private static bool IsManager(IGuildUser user)
    {
        return GuildAccessRules.CanManage(user.Guild.OwnerId == user.Id, user.GuildPermissions.Administrator, user.GuildPermissions.ManageGuild);
    }
}
