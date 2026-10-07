using System.Text;
using Discord;
using Discord.Interactions;

namespace DiscordBot.Discord.SlashCommands;

public class HelpModule : ModuleBase
{
    private readonly InteractionService _commands;

    public HelpModule(InteractionService commands)
    {
        _commands = commands;
    }

    [SlashCommand("help", "Show available commands")]
    public async Task HelpAsync()
    {
        var text = new StringBuilder("```\n📖 Available Commands\n\n");

        foreach (var command in _commands.SlashCommands.Where(CanUse).OrderBy(c => c.Name))
        {
            text.AppendLine($"/{command.Name.PadRight(22)} - {command.Description}");
        }

        text.Append("```");
        await RespondAsync(text.ToString());
    }

    /// <summary>Commands that need a permission are only listed to people who hold it (server administrators hold them all).</summary>
    private bool CanUse(SlashCommandInfo command)
    {
        if (command.DefaultMemberPermissions is not { } required)
        {
            return true;
        }

        return Context.User is IGuildUser user && (user.GuildPermissions.Administrator || user.GuildPermissions.Has(required));
    }
}
