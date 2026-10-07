using Discord.WebSocket;

namespace DiscordBot.Discord;

/// <summary>Lets the background workers wait until the bot is connected and ready, instead of polling the connection state.</summary>
public class DiscordConnection
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DiscordConnection(DiscordSocketClient client)
    {
        client.Ready += () =>
        {
            _ready.TrySetResult();
            return Task.CompletedTask;
        };
    }

    /// <summary>Completes the first time the bot is ready (and immediately after that).</summary>
    public Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        return _ready.Task.WaitAsync(cancellationToken);
    }
}
