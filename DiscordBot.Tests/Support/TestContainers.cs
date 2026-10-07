using Npgsql;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;

namespace DiscordBot.Tests.Support;

/// <summary>
/// The PostgreSQL and Kafka the tests run against. Each starts in a container the first time a test asks for it, is shared by
/// every test in the run, and is removed when the run ends. Only Docker is needed; nothing has to be started or configured first.
/// </summary>
public static class TestContainers
{
    private static readonly Lazy<Task<PostgreSqlContainer>> PostgresContainer = new(StartPostgresAsync);
    private static readonly Lazy<Task<KafkaContainer>> KafkaContainer = new(StartKafkaAsync);

    /// <summary>A connection string for the server's <c>postgres</c> database, whose user may create databases.</summary>
    public static async Task<string> PostgresAdminConnectionAsync()
    {
        var container = await PostgresContainer.Value;

        return new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = "postgres" }.ConnectionString;
    }

    public static async Task<string> KafkaBootstrapAsync()
    {
        return (await KafkaContainer.Value).GetBootstrapAddress();
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync()
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithCommand("-c", "max_connections=1000", "-c", "fsync=off")   // a throwaway database: speed over durability
            .Build();

        await container.StartAsync();
        return container;
    }

    private static async Task<KafkaContainer> StartKafkaAsync()
    {
        var container = new KafkaBuilder("apache/kafka:3.9.0").Build();

        await container.StartAsync();
        return container;
    }
}
