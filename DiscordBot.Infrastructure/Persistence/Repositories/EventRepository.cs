using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using DiscordBot.Domain.Enums;

namespace DiscordBot.Infrastructure.Persistence.Repositories;

public class EventRepository : IEventRepository
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public EventRepository(AppDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task CreateEventAsync(Event @event)
    {
        _context.Events.Add(@event);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateEventAsync(Event @event)
    {
        // An event loaded through this context is already tracked and saves only what changed. Update() would mark every column
        // as changed and overwrite whatever another request (or a worker) wrote to the others in the meantime.
        if (_context.Entry(@event).State == EntityState.Detached)
        {
            _context.Events.Update(@event);
        }

        await _context.SaveChangesAsync();
    }

    public async Task UpdateEventsAsync(IReadOnlyCollection<Event> events)
    {
        foreach (var evt in events.Where(e => _context.Entry(e).State == EntityState.Detached))
        {
            _context.Events.Update(evt);
        }

        await _context.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<Event>> GetUnannouncedEventsAsync(DateTime now, int limit)
    {
        return await _context.Events
            .Where(e => !e.IsDeleted && e.Status == EventStatus.Scheduled && e.ChannelId != null && e.MessageId == null && e.ScheduledTime > now)
            .OrderBy(e => e.ScheduledTime)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<Event?> GetEventByIdAsync(ulong eventId, CancellationToken cancellationToken = default)
    {
        return await _context.Events.FirstOrDefaultAsync(e => e.EventId == eventId, cancellationToken);
    }

    public async Task<Event?> GetEventByMessageIdAsync(ulong messageId)
    {
        return await _context.Events.FirstOrDefaultAsync(e => e.MessageId == messageId);
    }

    public async Task<IReadOnlyList<Event>> GetActiveEventsAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        return await _context.Events
            .AsNoTracking()
            .Where(e => e.GuildId == guildId && !e.IsDeleted && e.Status != EventStatus.Cancelled)
            .OrderBy(e => e.ScheduledTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Event>> GetUpcomingEventsAsync(ulong guildId)
    {
        var now = Now;

        return await _context.Events
            .AsNoTracking()
            .Where(e => e.GuildId == guildId && !e.IsDeleted && e.Status == EventStatus.Scheduled && e.ScheduledTime > now)
            .OrderBy(e => e.ScheduledTime)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Event>> GetEventsDueForReminderAsync(DateTime now, TimeSpan lookahead)
    {
        var horizon = now + lookahead;

        // Bounded on both sides so the scheduled-events index can be used and far-future events are never loaded
        return await _context.Events
            .Where(e => !e.IsDeleted && e.Status == EventStatus.Scheduled && !e.ReminderSent && e.ReminderMinutesBefore != null
                        && e.ScheduledTime > now && e.ScheduledTime <= horizon)
            .OrderBy(e => e.ScheduledTime)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Event>> GetStartedOneOffEventsAsync(DateTime now)
    {
        return await _context.Events
            .Where(e => !e.IsRecurring && !e.IsDeleted && e.Status == EventStatus.Scheduled && e.ScheduledTime <= now)
            .OrderBy(e => e.ScheduledTime)
            .Take(500)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Event>> GetDueRecurringEventsAsync()
    {
        var now = Now;

        return await _context.Events
            .Where(e => e.IsRecurring && !e.IsDeleted && e.Status == EventStatus.Scheduled && e.ScheduledTime <= now)
            .OrderBy(e => e.ScheduledTime)
            .ToListAsync();
    }

    public async Task<bool> CompleteRecurringEventAndCreateNextAsync(Event completed, Event next)
    {
        if (completed.Status != EventStatus.Scheduled)
        {
            return false;
        }

        // Single SaveChanges so the source event is never completed without its successor existing (or vice versa). The status is
        // a concurrency token, so if another worker completed the occurrence first this save fails and stores nothing.
        completed.Complete(Now);
        _context.Events.Add(next);

        try
        {
            await _context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Forget just these two: the caller may still be working through other tracked events
            _context.Entry(completed).State = EntityState.Detached;
            _context.Entry(next).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> TryClaimReminderAsync(Event evt)
    {
        if (evt.ReminderSent)
        {
            return false;
        }

        // ReminderSent is a concurrency token: of two workers that both saw "not sent", only the first save succeeds
        evt.MarkReminderSent(Now);

        try
        {
            await _context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.Entry(evt).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IReadOnlyList<Event>> GetEventsByParentIdAsync(ulong parentEventId)
    {
        return await _context.Events
            .Where(e => e.ParentRecurringEventId == parentEventId || e.EventId == parentEventId)
            .ToListAsync();
    }

    public async Task<GuildEventCounts> GetGuildEventCountsAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var now = Now;
        var events = _context.Events
            .AsNoTracking()
            .Where(e => e.GuildId == guildId && !e.IsDeleted && e.Status != EventStatus.Cancelled);

        var total = await events.CountAsync(cancellationToken);
        var upcoming = await events.CountAsync(e => e.ScheduledTime > now, cancellationToken);

        return new GuildEventCounts(total, upcoming);
    }
}
