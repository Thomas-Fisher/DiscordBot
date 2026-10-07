using DiscordBot.Infrastructure.EventBus;
using DiscordBot.Infrastructure.Persistence;
using DiscordBot.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Support;

/// <summary>A PostgreSQL database (see <see cref="TestDatabases"/>) of a test's own, with the repositories over one shared context.</summary>
public sealed class TestDb
{
    /// <param name="clock">What the repositories and the outbox treat as now; the real clock unless a test needs to control it.</param>
    public TestDb(TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;
        ConnectionString = TestDatabases.Create();
        Context = Open();
        Events = new EventRepository(Context, clock);
        Signups = new SignupRepository(Context);
        RememberedSpecs = new RememberedSpecRepository(Context);
        Guilds = new GuildRepository(Context);
        MessageLogs = new MessageLogRepository(Context, NullLogger<MessageLogRepository>.Instance);
        ActivityLogs = new ActivityLogRepository(Context, NullLogger<ActivityLogRepository>.Instance);
        Outbox = new Outbox(Context, new JsonEventSerializer(), new OutboxSignal(), clock);
    }

    public string ConnectionString { get; }

    public AppDbContext Context { get; }

    public EventRepository Events { get; }

    public SignupRepository Signups { get; }

    public RememberedSpecRepository RememberedSpecs { get; }

    public GuildRepository Guilds { get; }

    public MessageLogRepository MessageLogs { get; }

    public ActivityLogRepository ActivityLogs { get; }

    public Outbox Outbox { get; }

    /// <summary>A new context on the same database, for reading back what was stored or for a second worker.</summary>
    public AppDbContext Open()
    {
        return TestDatabases.Open(ConnectionString);
    }
}
