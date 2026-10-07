using DiscordBot.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Infrastructure.Persistence.Repositories;

public class ProcessedEventRepository : IProcessedEventRepository
{
    private readonly AppDbContext _context;

    public ProcessedEventRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<bool> HasProcessedAsync(Guid eventId, string handler)
    {
        return await _context.ProcessedEvents.AnyAsync(p => p.EventId == eventId && p.Handler == handler);
    }

    public async Task MarkProcessedAsync(Guid eventId, string handler)
    {
        // Recording twice (a redelivery racing the first delivery) must not fail, so let the database ignore the duplicate
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"ProcessedEvents\" (\"EventId\", \"Handler\", \"ProcessedAt\") VALUES ({eventId}, {handler}, {DateTime.UtcNow}) ON CONFLICT (\"EventId\", \"Handler\") DO NOTHING");
    }

    public async Task DeleteOlderThanAsync(int days)
    {
        await BatchedDelete.OlderThanAsync(_context, "ProcessedEvents", "ProcessedAt", DateTime.UtcNow.AddDays(-days));
    }
}
