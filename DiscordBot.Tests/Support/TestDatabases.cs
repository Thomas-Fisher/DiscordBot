using DiscordBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DiscordBot.Tests.Support;

/// <summary>
/// Throwaway PostgreSQL databases for tests. The schema is built once, by applying the migrations, into a template; each test
/// then gets its own database copied from it, which is fast and means no test can see another's rows.
/// </summary>
public static class TestDatabases
{
    private const string TemplateName = "discordbot_template";

    private static readonly object CreateLock = new();
    private static readonly Lazy<string> AdminConnection = new(BuildTemplate);

    public static string Create(string prefix = "test")
    {
        var admin = AdminConnection.Value;
        var name = $"{prefix}_{Guid.NewGuid():N}";

        // One at a time: PostgreSQL will not copy a template that another session is copying
        lock (CreateLock)
        {
            using var connection = new NpgsqlConnection(admin);
            connection.Open();
            using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\" TEMPLATE \"{TemplateName}\"", connection);
            create.ExecuteNonQuery();
        }

        // No pooling: a test run opens thousands of databases, and idle pooled connections to them would exhaust the server
        return new NpgsqlConnectionStringBuilder(admin) { Database = name, Pooling = false }.ConnectionString;
    }

    public static AppDbContext Open(string connectionString)
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure())   // as in production, so code with its own transaction has to cope with the retrying strategy
            .AddInterceptors(new EnsureGuildsInterceptor())
            .Options);
    }

    private static string BuildTemplate()
    {
        var admin = new NpgsqlConnectionStringBuilder(TestContainers.PostgresAdminConnectionAsync().GetAwaiter().GetResult()) { Pooling = false }.ConnectionString;

        using (var connection = new NpgsqlConnection(admin))
        {
            connection.Open();
            using var create = new NpgsqlCommand($"CREATE DATABASE \"{TemplateName}\"", connection);
            create.ExecuteNonQuery();
        }

        var template = new NpgsqlConnectionStringBuilder(admin) { Database = TemplateName }.ConnectionString;
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(template).Options);
        context.Database.Migrate();

        return admin;
    }
}
