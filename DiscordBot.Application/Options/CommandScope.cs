namespace DiscordBot.Application.Options;

public enum CommandScope
{
    /// <summary>Everywhere the bot is. Discord can take up to an hour to show a change.</summary>
    Global,

    /// <summary>Server by server, for the servers the bot is in. Changes show at once, which is what you want while developing.</summary>
    Guild
}
