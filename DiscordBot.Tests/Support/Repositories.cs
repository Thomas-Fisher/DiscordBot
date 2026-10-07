using DiscordBot.Infrastructure.Persistence;
using DiscordBot.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Support;

public sealed class Repositories
{
    public Repositories(AppDbContext context, TimeProvider? clock = null)
    {
        Events = new EventRepository(context, clock ?? TimeProvider.System);
        Signups = new SignupRepository(context);
        MessageLogs = new MessageLogRepository(context, NullLogger<MessageLogRepository>.Instance);
        ActivityLogs = new ActivityLogRepository(context, NullLogger<ActivityLogRepository>.Instance);
    }

    public EventRepository Events { get; }

    public SignupRepository Signups { get; }

    public MessageLogRepository MessageLogs { get; }

    public ActivityLogRepository ActivityLogs { get; }
}
