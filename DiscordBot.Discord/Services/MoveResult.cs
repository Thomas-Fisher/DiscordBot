namespace DiscordBot.Discord.Services;

public record MoveResult(bool Success, string? Error = null)
{
    public static MoveResult Ok() => new(true);

    public static MoveResult Fail(string error) => new(false, error);
}
