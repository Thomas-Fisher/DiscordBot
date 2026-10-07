namespace DiscordBot.Application;

/// <summary>
/// Who may manage a guild's bot, in the dashboard and in Discord commands alike: its owner, or anyone with the Administrator or
/// Manage Server permission.
/// </summary>
public static class GuildAccessRules
{
    // Discord's permission bit flags
    public const long Administrator = 0x8;
    public const long ManageGuild = 0x20;

    public static bool CanManage(bool isOwner, bool hasAdministrator, bool hasManageGuild)
    {
        return isOwner || hasAdministrator || hasManageGuild;
    }

    public static bool CanManage(bool isOwner, long permissions)
    {
        return CanManage(isOwner, (permissions & Administrator) != 0, (permissions & ManageGuild) != 0);
    }
}
