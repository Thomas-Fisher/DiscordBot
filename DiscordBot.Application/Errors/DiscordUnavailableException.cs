namespace DiscordBot.Application.Errors;

/// <summary>Discord did not answer, or answered with an error, when the app needed it (the sign-in, for example). Trying again may work.</summary>
public class DiscordUnavailableException : Exception
{
    public DiscordUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
