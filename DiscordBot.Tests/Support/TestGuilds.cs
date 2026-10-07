using DiscordBot.Application.Models;

namespace DiscordBot.Tests.Support;

public static class TestGuilds
{
    /// <summary>A guild the bot is in. Tests that care about a particular detail (owner, member count) build their own.</summary>
    public static GuildInfo Basic(ulong id, string name = "Raiders") =>
        new(id, name, IconId: null, Description: null, MemberCount: 1, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc), OwnerId: 1, BoostLevel: 0, RoleCount: 1, ChannelCount: 1);
}
