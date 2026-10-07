namespace DiscordBot.Application.Models;

public record ActivityContext(ActivityOrigin Origin, ulong? UserId)
{
    public static ActivityContext Discord(ulong userId) => new(ActivityOrigin.Discord, userId);

    public static ActivityContext Api(ulong? userId) => new(ActivityOrigin.Api, userId);

    public static ActivityContext System() => new(ActivityOrigin.System, null);
}
