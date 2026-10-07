using DiscordBot.Application;
using DiscordBot.Application.Interfaces;
using DiscordBot.Infrastructure.EventBus;
using DiscordBot.Discord.DiscordEventHandlers;
using DiscordBot.Infrastructure.Background;
using Microsoft.Extensions.Hosting;
using DiscordBot.Discord;
using DiscordBot.Discord.SlashCommands;
using DiscordBot.Infrastructure;
using Discord.Interactions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot.Tests.Unit.Composition;

/// <summary>
/// Builds the real container the way Program.cs does and checks it is wired correctly, so a missing registration or a
/// lifetime mistake fails here rather than on the first Discord interaction.
/// </summary>
public class CompositionTests
{
    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused",
                ["Jwt:Secret"] = new string('s', 40),
                ["Jwt:Issuer"] = "test",
                ["Jwt:Audience"] = "test",
                ["Discord:ClientId"] = "client",
                ["Discord:ClientSecret"] = "secret",
                ["Discord:RedirectUri"] = "http://localhost/callback",
                ["Discord:BotToken"] = "not-a-real-token"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Test" });   // the real host provides this
        services.AddInfrastructure(configuration);
        services.AddApplication(configuration);
        services.AddDiscordBot();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public void CreateInstance_EverySlashCommandModule_CanHaveItsConstructorFilledFromAScope()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var modules = typeof(ModuleBase).Assembly.GetTypes()
            .Where(t => typeof(ModuleBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        var instances = modules.Select(m => ActivatorUtilities.CreateInstance(scope.ServiceProvider, m)).ToList();

        Assert.NotEmpty(modules);
        Assert.Equal(modules.Count, instances.Count);
    }

    [Fact]
    public async Task InteractionService_LoadsEveryModule_WithUniqueCommandsAndNoneBrokenByItsAttributesOrParameters()
    {
        using var provider = BuildProvider();
        var interactions = provider.GetRequiredService<InteractionService>();

        // Discord.Net checks each command's name, options and component ids while it loads the modules
        await interactions.AddModulesAsync(typeof(ModuleBase).Assembly, provider);

        var commands = interactions.SlashCommands.Select(c => c.Name).ToList();
        Assert.Equal(commands.Count, commands.Distinct().Count());
        // Discord refuses to register any command whose description, or one of whose options' descriptions, is over 100 characters
        Assert.All(interactions.SlashCommands, command =>
        {
            Assert.True(command.Description.Length <= 100, $"/{command.Name}: {command.Description.Length} characters");
            Assert.All(command.Parameters, parameter => Assert.True(parameter.Description.Length <= 100, $"/{command.Name} {parameter.Name}: {parameter.Description.Length} characters"));
        });
        Assert.Superset(new HashSet<string> { "createevent", "createrecurringevent", "editevent", "cancelevent", "cancelrecurringevent", "deleteevent", "moveevent", "moveeventhere", "listevents", "timezone", "help", "ping", "about" }, commands.ToHashSet());
    }

    [Fact]
    public void BuildServiceProvider_ProducesAContainerWithNoMissingServicesOrLifetimeMistakes()
    {
        var exception = Record.Exception(() => BuildProvider().Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void BuildServiceProvider_HoldsTheGatewayAndRunsTheRelayAndTheConsumer()
    {
        using var provider = BuildProvider();
        var background = provider.GetServices<IHostedService>().Select(s => s.GetType()).ToList();

        Assert.IsType<DiscordBot.Discord.Services.DiscordGuildDirectory>(provider.GetRequiredService<IGuildDirectory>());
        Assert.IsType<DiscordBot.Discord.Services.EventMessageRefresher>(provider.GetRequiredService<IEventMessageUpdater>());
        Assert.Contains(typeof(OutboxRelayService), background);
        Assert.Contains(typeof(KafkaEventConsumerService), background);
        Assert.Contains(typeof(DiscordBot.Discord.DiscordClientService), background);
    }

    [Fact]
    public void CreateInstance_EveryController_CanHaveItsConstructorFilledFromAScope()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var controllers = typeof(DiscordBot.Api.Controllers.BaseApiController).Assembly.GetTypes()
            .Where(t => typeof(DiscordBot.Api.Controllers.BaseApiController).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        var instances = controllers.Select(c => ActivatorUtilities.CreateInstance(scope.ServiceProvider, c)).ToList();

        Assert.NotEmpty(controllers);
        Assert.Equal(controllers.Count, instances.Count);
    }

    private static string QueueNameUsedBy(object consumer)
    {
        var field = consumer.GetType().GetField("_backgroundQueue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return ((BackgroundTaskQueue)field.GetValue(consumer)!).Name;
    }

    [Fact]
    public void BuildServiceProvider_BackgroundQueues_EachLaneHasItsOwnQueueAndWorker()
    {
        using var provider = BuildProvider();

        var lanes = BackgroundQueues.All.Select(name => (BackgroundTaskQueue)provider.GetRequiredKeyedService<IBackgroundTaskQueue>(name)).ToList();
        var workers = provider.GetServices<IHostedService>().OfType<BackgroundTaskQueue>().ToList();

        Assert.Equal(BackgroundQueues.All, lanes.Select(q => q.Name));
        Assert.Equal(3, workers.Count);
        Assert.Same(provider.GetRequiredKeyedService<IBackgroundTaskQueue>(BackgroundQueues.Work), provider.GetRequiredService<IBackgroundTaskQueue>());
    }

    [Fact]
    public void BuildServiceProvider_EventPublishers_UseThePublishQueueAndSlowJobsTheMaintenanceQueue()
    {
        using var provider = BuildProvider();
        var handlers = provider.GetServices<IGatewayHandler>().ToDictionary(h => h.GetType().Name);
        using var scope = provider.CreateScope();

        Assert.Equal(BackgroundQueues.Publish, QueueNameUsedBy(handlers["ReactionAddedHandler"]));
        Assert.Equal(BackgroundQueues.Publish, QueueNameUsedBy(handlers["MessageReceivedHandler"]));
        Assert.Equal(BackgroundQueues.Maintenance, QueueNameUsedBy(handlers["MissedReactionRecoveryService"]));
        Assert.Equal(BackgroundQueues.Maintenance, QueueNameUsedBy(handlers["GuildSyncHandler"]));
        Assert.Equal(BackgroundQueues.Work, QueueNameUsedBy(scope.ServiceProvider.GetRequiredService<IEventMessageUpdater>()));
    }
}
