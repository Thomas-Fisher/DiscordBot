namespace DiscordBot.Application.Errors;

/// <summary>What sort of refusal an <see cref="Error"/> is, which decides how it is reported (for the API, the status code).</summary>
public enum ErrorKind
{
    Invalid,

    /// <summary>What the request is about does not exist (or is not visible to the caller).</summary>
    NotFound
}
