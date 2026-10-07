using DiscordBot.Application;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Options;
using DiscordBot.Infrastructure.Auth;
using DiscordBot.Infrastructure.Background;
using DiscordBot.Infrastructure.EventBus;
using DiscordBot.Infrastructure.Logging;
using DiscordBot.Infrastructure.Persistence;
using DiscordBot.Infrastructure.Persistence.Repositories;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("DefaultConnection not configured"),
                npgsql => npgsql
                    .EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null)
                    .CommandTimeout(30)));

        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<ISignupRepository, SignupRepository>();
        services.AddScoped<IRememberedSpecRepository, RememberedSpecRepository>();
        services.AddScoped<IGuildRepository, GuildRepository>();
        services.AddScoped<IMessageLogRepository, MessageLogRepository>();
        services.AddScoped<IActivityLogRepository, ActivityLogRepository>();
        services.AddScoped<IActivityLogger, ActivityLogger>();
        services.AddScoped<IProcessedEventRepository, ProcessedEventRepository>();
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

        // Events that must not be lost go through the outbox: written to the database, published to the bus by the relay
        services.AddSingleton<IEventSerializer, JsonEventSerializer>();
        services.AddSingleton<OutboxSignal>();
        services.AddScoped<IOutbox, Outbox>();
        services.AddHealthChecks().AddCheck<OutboxHealthCheck>("outbox");

        AddEventBus(services, configuration);
        AddBackgroundWork(services);

        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ITokenService, TokenService>();
        // Timeouts, a few retries for temporary failures, and a circuit breaker for when Discord is down
        services.AddHttpClient<IDiscordOAuthClient, DiscordOAuthClient>().AddStandardResilienceHandler();

        return services;
    }

    private static void AddEventBus(IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<EventBusOptions>(configuration, EventBusOptions.SectionName);
        services.AddSingleton<IEventDispatcher, EventDispatcher>();
        services.AddHostedService<OutboxRelayService>();

        var eventBus = configuration.GetSection(EventBusOptions.SectionName).Get<EventBusOptions>() ?? new EventBusOptions();
        AddKafkaEventBus(services, eventBus.Kafka);
    }

    private static void AddBackgroundWork(IServiceCollection services)
    {
        // Background work queues (keep slow work off the Discord gateway thread), one worker each so they cannot hold each other up
        foreach (var lane in BackgroundQueues.All)
        {
            services.AddKeyedSingleton<IBackgroundTaskQueue>(lane, (sp, key) =>
                new BackgroundTaskQueue((string)key!, sp.GetRequiredService<ILoggerFactory>().CreateLogger<BackgroundTaskQueue>()));

            // AddSingleton, not AddHostedService: that one skips a second registration of the same type
            services.AddSingleton<IHostedService>(sp => (BackgroundTaskQueue)sp.GetRequiredKeyedService<IBackgroundTaskQueue>(lane));
        }

        services.AddSingleton<IBackgroundTaskQueue>(sp => sp.GetRequiredKeyedService<IBackgroundTaskQueue>(BackgroundQueues.Work));
        services.AddHealthChecks().AddCheck<BackgroundQueuesHealthCheck>("backgroundQueues");

        services.AddSingleton<MessageLogBatchWriter>();
        services.AddSingleton<IMessageLogWriter>(sp => sp.GetRequiredService<MessageLogBatchWriter>());
        services.AddHostedService(sp => sp.GetRequiredService<MessageLogBatchWriter>());

        services.AddHostedService<DataRetentionService>();
        services.AddHostedService<EventCompletionService>();
    }

    private static void AddKafkaEventBus(IServiceCollection services, KafkaOptions kafka)
    {
        services.AddSingleton(new KafkaTopics(kafka));
        services.AddSingleton<KafkaTopicProvisioner>();
        services.AddSingleton<KafkaConsumerStatus>();

        // One producer for the whole app (the client is thread-safe and batches messages internally)
        services.AddSingleton<IProducer<string, string>>(_ => new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = kafka.ProduceTimeoutSeconds * 1000,
            LingerMs = 5,
            CompressionType = CompressionType.Lz4
        }).Build());

        services.AddSingleton<IEventBus, KafkaEventBus>();
        services.AddSingleton<IDeadLetterSink, KafkaDeadLetterSink>();
        services.AddSingleton<KafkaMessageProcessor>();
        services.AddHostedService<KafkaEventConsumerService>();
        services.AddHostedService<KafkaProducerLifetime>();
        services.AddHealthChecks().AddCheck<KafkaHealthCheck>("eventBus");
    }
}
