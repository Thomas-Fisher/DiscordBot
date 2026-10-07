using DiscordBot.Application;
using Discord.WebSocket;
using DiscordBot.Application.Interfaces;
using DiscordBot.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord.DiscordEventHandlers;

public class GuildSyncHandler : IGatewayHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly ILogger<GuildSyncHandler> _logger;
    private DiscordSocketClient _client = null!;

    public GuildSyncHandler(IServiceScopeFactory scopeFactory, [FromKeyedServices(BackgroundQueues.Maintenance)] IBackgroundTaskQueue backgroundQueue, ILogger<GuildSyncHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _backgroundQueue = backgroundQueue;
        _logger = logger;
    }

    public void Attach(DiscordSocketClient client)
    {
        _client = client;
        client.Ready += () =>
        {
            _backgroundQueue.Enqueue(nameof(GuildSyncHandler), _ => SyncAsync());
            return Task.CompletedTask;
        };

        client.JoinedGuild += guild =>
        {
            _backgroundQueue.Enqueue(nameof(client.JoinedGuild), _ => SaveGuildAsync(guild));
            return Task.CompletedTask;
        };

        client.GuildUpdated += (before, after) =>
        {
            if (before.Name != after.Name)
            {
                _backgroundQueue.Enqueue(nameof(client.GuildUpdated), _ => SaveGuildAsync(after));
            }

            return Task.CompletedTask;
        };

        client.LeftGuild += guild =>
        {
            // The guild's events and logs are kept (and age out through the retention settings) in case the bot is re-invited
            _logger.LogInformation("Left guild {GuildId} ({GuildName})", guild.Id, guild.Name);
            return Task.CompletedTask;
        };
    }

    private async Task SaveGuildAsync(SocketGuild guild)
    {
        using var scope = _scopeFactory.CreateScope();
        await SaveGuildAsync(scope.ServiceProvider.GetRequiredService<IGuildRepository>(), guild);
    }

    private static async Task SaveGuildAsync(IGuildRepository guilds, SocketGuild guild)
    {
        var now = DateTime.UtcNow;
        await guilds.SaveGuildAsync(new Guild { GuildId = guild.Id, GuildName = guild.Name, CreatedAt = now, UpdatedAt = now });
    }

    private async Task SyncAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var guilds = scope.ServiceProvider.GetRequiredService<IGuildRepository>();

        foreach (var guild in _client.Guilds)
        {
            await SaveGuildAsync(guilds, guild);
        }

        _logger.LogInformation("Synced {GuildCount} guilds to database", _client.Guilds.Count);
    }
}
