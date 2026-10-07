using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Application.Interfaces;
using DiscordBot.Discord.BackgroundServices;
using DiscordBot.Discord.DiscordEventHandlers;
using DiscordBot.Discord.DomainEventHandlers;
using DiscordBot.Discord.Services;
using DiscordBot.Discord.Services.WoW;
using DiscordBot.Domain.Events;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot.Discord;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the Discord adapter. The slash command modules are not registered here: the interaction service builds them
    /// per interaction from that interaction's scope.
    /// </summary>
    public static IServiceCollection AddDiscordBot(this IServiceCollection services)
    {
        // The socket client and the interaction (slash command) service. Commands run synchronously inside
        // InteractionHandler's per-interaction DI scope, which is what lets modules take scoped services in constructors.
        services.AddSingleton(_ => new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = RequiredIntents,
            AlwaysDownloadUsers = true,   // needs the Server Members intent; makes access checks cache lookups
            LogLevel = global::Discord.LogSeverity.Info
        }));
        services.AddSingleton(sp => new InteractionService(
            sp.GetRequiredService<DiscordSocketClient>(),
            new InteractionServiceConfig { DefaultRunMode = RunMode.Sync }));

        services.AddSingleton<IGatewayHandler, InteractionHandler>();
        services.AddSingleton<IGatewayHandler, GuildSyncHandler>();
        services.AddSingleton<IGatewayHandler, DiscordActivityAuditor>();
        services.AddSingleton<IGatewayHandler, MessageReceivedHandler>();
        services.AddSingleton<IGatewayHandler, ReactionAddedHandler>();
        services.AddSingleton<IGatewayHandler, MissedReactionRecoveryService>();
        services.AddSingleton<IGatewayHandler, WowEmoteSync>();
        services.AddSingleton<IApplicationEmoteStore, DiscordApplicationEmoteStore>();

        services.AddSingleton<DiscordConnection>();
        services.AddSingleton<DiscordClientService>();
        services.AddHostedService(sp => sp.GetRequiredService<DiscordClientService>());
        services.AddSingleton<IDiscordMessageSender, DiscordMessageSender>();
        services.AddHealthChecks().AddCheck<DiscordHealthCheck>("discord");
        services.AddSingleton<IGuildAccessService, DiscordGuildAccessService>();
        services.AddSingleton<IGuildDirectory, DiscordGuildDirectory>();

        services.AddSingleton<InteractionState>();
        services.AddSingleton<ISpecPrompter, DiscordSpecPrompter>();
        services.AddSingleton<EventMessageRefresher>();
        services.AddSingleton<IEventMessageUpdater>(sp => sp.GetRequiredService<EventMessageRefresher>());
        services.AddSingleton<DiscordEventAnnouncer>();
        services.AddSingleton<IEventPingSender, DiscordEventPingSender>();
        services.AddSingleton<IEventAnnouncer>(sp => sp.GetRequiredService<DiscordEventAnnouncer>());
        services.AddSingleton<EventMessageMover>();
        services.AddSingleton<EventPermissionService>();

        // Event handlers that act on Discord (the dispatcher opens a scope per event)
        services.AddScoped<IEventHandler<ReactionAddedEvent>, EventSignupHandler>();
        services.AddScoped<IEventHandler<ReactionAddedEvent>, UnauthorizedReactionHandler>();
        services.AddScoped<IEventHandler<ActivityLogCreatedEvent>, ActivityLogNotificationHandler>();

        services.AddSingleton<EventReminderService>();
        services.AddHostedService(sp => sp.GetRequiredService<EventReminderService>());
        services.AddSingleton<RecurringEventService>();
        services.AddHostedService(sp => sp.GetRequiredService<RecurringEventService>());

        return services;
    }

    /// <summary>
    /// Only the events the bot uses. MessageContent (message logging) and GuildMembers (join/leave logging) are privileged:
    /// both must be switched on for the bot in the Discord developer portal or Discord refuses the connection.
    /// </summary>
    public const global::Discord.GatewayIntents RequiredIntents = global::Discord.GatewayIntents.Guilds
        | global::Discord.GatewayIntents.GuildMessages
        | global::Discord.GatewayIntents.GuildMessageReactions
        | global::Discord.GatewayIntents.GuildVoiceStates
        | global::Discord.GatewayIntents.GuildMembers
        | global::Discord.GatewayIntents.MessageContent;
}
