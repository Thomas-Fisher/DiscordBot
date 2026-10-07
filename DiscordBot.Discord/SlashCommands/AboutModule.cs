using System.Runtime.InteropServices;
using Discord.Interactions;
using DiscordBot.Discord.Services;

namespace DiscordBot.Discord.SlashCommands;

public class AboutModule : ModuleBase
{
    [SlashCommand("about", "Show bot information")]
    public Task AboutAsync()
    {
        var text = $"""
            ```
            🤖 Discord Bot

            Author: Thomas Fisher
            Library: Discord.Net v{BotInfo.DiscordNetVersion}
            Runtime: {RuntimeInformation.FrameworkDescription}
            OS: {RuntimeInformation.OSDescription}

            Status:
              Uptime: {BotInfo.FormatUptime(BotInfo.Uptime)}
              Memory: {BotInfo.FormatBytes(GC.GetTotalMemory(false))}

            Description:
              Message Logger & Event Scheduler
              Version: {BotInfo.Version}

            Built with:
              Discord.Net, Entity Framework, PostgreSQL, Kafka
            ```
            """;

        return RespondAsync(text);
    }
}
