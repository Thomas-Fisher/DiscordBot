using Discord;
using Discord.Net;
using Discord.WebSocket;
using DiscordBot.Discord.DiscordEventHandlers;
using DiscordBot.Application.Options;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord;

public class DiscordClientService : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly ILogger<DiscordClientService> _logger;
    private readonly string _token;
    private readonly bool _enabled;

    public DiscordClientService(
        DiscordSocketClient client,
        ILogger<DiscordClientService> logger,
        IOptions<DiscordOptions> options,
        IEnumerable<IGatewayHandler> handlers)
    {
        _client = client;
        _logger = logger;
        _token = options.Value.BotToken;
        _enabled = options.Value.Enabled;

        _client.Log += LogAsync;
        _client.Disconnected += exception =>
        {
            var hint = IntentsHint(exception);
            if (hint != null)
            {
                _logger.LogCritical("{Hint}", hint);
            }

            return Task.CompletedTask;
        };
        _client.Ready += () =>
        {
            _logger.LogInformation("Bot connected as {BotName}", _client.CurrentUser?.Username);
            return Task.CompletedTask;
        };

        // Attach before connecting so no event is missed
        foreach (var handler in handlers)
        {
            handler.Attach(_client);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            _logger.LogWarning("The Discord connection is switched off (Discord:Enabled is false); only the API is running");
            return;
        }

        _logger.LogInformation("Starting Discord client...");
        await _client.LoginAsync(TokenType.Bot, _token);
        await _client.StartAsync();
        _logger.LogInformation("Discord client started");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Stopping Discord client...");
            await _client.StopAsync();
            _logger.LogInformation("Discord client stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping Discord client");
        }
    }

    /// <summary>
    /// Discord closes the connection with 4013/4014 when the bot asks for an intent that is not switched on in the developer
    /// portal. The raw error says little, so translate it. Returns null for any other disconnect.
    /// </summary>
    public static string? IntentsHint(Exception? exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is WebSocketClosedException { CloseCode: 4013 or 4014 })
            {
                return "Discord refused the connection because the bot uses an intent that is not enabled. Open the Discord developer portal, "
                    + "go to your application > Bot > Privileged Gateway Intents, and switch on both Message Content Intent and Server Members Intent.";
            }
        }

        return null;
    }

    private Task LogAsync(LogMessage log)
    {
        var level = log.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            _ => LogLevel.Debug
        };

        _logger.Log(level, log.Exception, "[{Severity}] {Source}: {Message}", log.Severity, log.Source, log.Message);
        return Task.CompletedTask;
    }
}
