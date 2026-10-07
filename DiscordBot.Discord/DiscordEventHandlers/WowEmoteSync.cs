using Discord.WebSocket;
using DiscordBot.Application;
using DiscordBot.Application.Interfaces;
using DiscordBot.Discord.Services.WoW;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord.DiscordEventHandlers;

public class WowEmoteSync : IGatewayHandler
{
    private readonly IApplicationEmoteStore _store;
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly ILogger<WowEmoteSync> _logger;
    private readonly Action<IReadOnlyDictionary<string, ulong>> _register;

    public WowEmoteSync(IApplicationEmoteStore store, [FromKeyedServices(BackgroundQueues.Maintenance)] IBackgroundTaskQueue backgroundQueue, ILogger<WowEmoteSync> logger, Action<IReadOnlyDictionary<string, ulong>>? register = null)
    {
        _store = store;
        _backgroundQueue = backgroundQueue;
        _logger = logger;
        _register = register ?? WowEmotes.Register;
    }

    public void Attach(DiscordSocketClient client)
    {
        client.Ready += () =>
        {
            _backgroundQueue.Enqueue(nameof(WowEmoteSync), _ => SyncAsync());
            return Task.CompletedTask;
        };
    }

    public async Task SyncAsync()
    {
        var ids = new Dictionary<string, ulong>(await _store.ListAsync());
        _register(new Dictionary<string, ulong>(ids));

        var missing = WowEmoteIcons.Names.Where(name => !ids.ContainsKey(name)).ToList();
        if (missing.Count == 0)
        {
            _logger.LogInformation("All {Count} class and spec emotes already exist", ids.Count);
            return;
        }

        _logger.LogInformation("Uploading {Count} class and spec emotes", missing.Count);

        foreach (var name in missing)
        {
            try
            {
                await using var image = WowEmoteIcons.Open(name);
                ids[name] = await _store.CreateAsync(name, image);
                _register(new Dictionary<string, ulong>(ids));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upload the emote {Name}", name);
            }
        }

        _logger.LogInformation("Class and spec emotes are ready");
    }
}
