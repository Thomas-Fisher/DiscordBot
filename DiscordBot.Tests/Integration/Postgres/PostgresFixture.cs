using DiscordBot.Infrastructure.Persistence;
using DiscordBot.Tests.Support;

namespace DiscordBot.Tests.Integration.Postgres;

/// <summary>A database of its own, with the schema from the migrations (see <see cref="TestDatabases"/>), for tests that share it.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public Task InitializeAsync()
    {
        ConnectionString = TestDatabases.Create("dbtest");

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    public AppDbContext CreateContext()
    {
        return TestDatabases.Open(ConnectionString);
    }
}
