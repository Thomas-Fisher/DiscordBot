using Discord.Interactions;
using DiscordBot.Application.Options;
using DiscordBot.Discord.Constants;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordBot.Discord.DiscordEventHandlers;

/// <summary>
/// Registers the slash commands when the bot is ready and runs every interaction in its own DI scope, so modules can take
/// scoped services.
/// </summary>
public class InteractionHandler : IGatewayHandler
{
    private readonly InteractionService _interactionService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InteractionHandler> _logger;
    private readonly CommandScope _scope;
    private DiscordSocketClient _client = null!;
    private int _registered;

    public InteractionHandler(InteractionService interactionService, IServiceScopeFactory scopeFactory, IOptions<DiscordOptions> options, ILogger<InteractionHandler> logger)
    {
        _scope = options.Value.CommandScope;
        _interactionService = interactionService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Attach(DiscordSocketClient client)
    {
        _client = client;
        client.Ready += RegisterCommandsAsync;
        client.InteractionCreated += OnInteractionCreated;

        // Registered server by server, a server that is added later would otherwise have no commands until the next restart
        client.JoinedGuild += guild => RegisterToGuildAsync(guild.Id);
    }

    private async Task RegisterToGuildAsync(ulong guildId)
    {
        if (_scope != CommandScope.Guild)
        {
            return;
        }

        try
        {
            await _interactionService.RegisterCommandsToGuildAsync(guildId);
            _logger.LogInformation("Commands registered for guild {GuildId}", guildId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering slash commands for guild {GuildId}", guildId);
        }
    }

    private async Task RegisterCommandsAsync()
    {
        // Ready fires again after every full reconnect; the commands do not change while the bot runs, so once is enough
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        try
        {
            if (_scope == CommandScope.Guild)
            {
                foreach (var guild in _client.Guilds)
                {
                    await _interactionService.RegisterCommandsToGuildAsync(guild.Id);
                    _logger.LogInformation("Commands registered for guild {GuildId}", guild.Id);
                }
            }
            else
            {
                await _interactionService.RegisterCommandsGloballyAsync();
                _logger.LogInformation("Slash commands registered globally (Discord can take up to an hour to show a change)");
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _registered, 0);   // try again the next time the bot is ready
            _logger.LogError(ex, "Error registering slash commands");
        }
    }

    private Task OnInteractionCreated(SocketInteraction interaction)
    {
        // Don't hold up the gateway while a command runs; interactions are independent, so they run concurrently
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = new SocketInteractionContext(_client, interaction);

                // The interaction service runs commands synchronously (see Program), so the scope outlives the command
                var result = await _interactionService.ExecuteCommandAsync(context, scope.ServiceProvider);

                if (!result.IsSuccess)
                {
                    // In synchronous mode Discord.Net catches what a command throws and hands it back here, so this is where it is logged
                    if (result is ExecuteResult { Exception: { } exception })
                    {
                        _logger.LogError(exception, "Interaction failed: {Error} ({ErrorType})", result.ErrorReason, result.Error);
                    }
                    else
                    {
                        _logger.LogWarning("Interaction failed: {Error} ({ErrorType})", result.ErrorReason, result.Error);
                    }

                    // A failed precondition (for example "guild only") has a message meant for the user; anything else does not
                    var text = result.Error == InteractionCommandError.UnmetPrecondition ? result.ErrorReason : UserMessages.ErrorProcessingRequest;
                    await TellUserAsync(interaction, text);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling interaction");
                await TellUserAsync(interaction, UserMessages.ErrorProcessingRequest);
            }
        });

        return Task.CompletedTask;
    }

    /// <summary>Without a reply Discord shows "The application did not respond"; say something useful instead.</summary>
    private async Task TellUserAsync(SocketInteraction interaction, string text)
    {
        try
        {
            if (interaction.HasResponded)
            {
                await interaction.FollowupAsync(text, ephemeral: true);
            }
            else
            {
                await interaction.RespondAsync(text, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            // Most likely the interaction has expired
            _logger.LogDebug(ex, "Could not tell the user that the interaction failed");
        }
    }
}
