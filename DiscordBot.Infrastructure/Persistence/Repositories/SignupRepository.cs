using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Common;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Infrastructure.Persistence.Repositories;

public class SignupRepository : ISignupRepository
{
    private readonly AppDbContext _context;

    public SignupRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<EventSignup>> GetEventSignupsAsync(ulong eventId, CancellationToken cancellationToken = default)
    {
        return await _context.EventSignups.AsNoTracking().Where(s => s.EventId == eventId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<ulong, SignupCounts>> GetSignupCountsAsync(IReadOnlyCollection<ulong> eventIds, CancellationToken cancellationToken = default)
    {
        var rows = await _context.EventSignups.AsNoTracking()
            .Where(s => eventIds.Contains(s.EventId) && s.Status != SignupStatus.SignedOff)
            .GroupBy(s => new { s.EventId, s.Status })
            .Select(g => new { g.Key.EventId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.EventId)
            .ToDictionary(
                g => g.Key,
                g => new SignupCounts(
                    g.Where(r => r.Status == SignupStatus.SignedUp).Sum(r => r.Count),
                    g.Where(r => r.Status == SignupStatus.Unsure).Sum(r => r.Count)));
    }

    public async Task<EventSignup?> GetEventSignupAsync(ulong eventId, ulong userId)
    {
        return await _context.EventSignups.FirstOrDefaultAsync(s => s.EventId == eventId && s.UserId == userId);
    }

    public async Task<int?> SignUpAsync(ulong eventId, ulong userId, string? wowClass = null, string? wowSpec = null)
    {
        var hasSelection = !string.IsNullOrEmpty(wowClass) && !string.IsNullOrEmpty(wowSpec);

        // Serialise signups per event so two people signing up at once cannot be given the same order number
        return await RunWithEventLockAsync(eventId, async () =>
        {
            var signup = await GetEventSignupAsync(eventId, userId);

            // Anyone with a class and spec (given now, or kept from an earlier signup) has a place in the line
            var className = hasSelection ? wowClass : signup?.WowClass;
            var specName = hasSelection ? wowSpec : signup?.WowSpec;
            var hasPlace = !string.IsNullOrEmpty(className) && !string.IsNullOrEmpty(specName);

            int? signupOrder = null;
            if (hasPlace)
            {
                if (signup?.Status == SignupStatus.SignedUp && signup.SignupOrder.HasValue)
                {
                    signupOrder = signup.SignupOrder;
                }
                else
                {
                    // New, or coming back after signing off / being unsure: join the end of the line
                    var currentMax = await _context.EventSignups
                        .Where(s => s.EventId == eventId && s.Status == SignupStatus.SignedUp)
                        .MaxAsync(s => (int?)s.SignupOrder);
                    signupOrder = (currentMax ?? 0) + 1;
                }
            }

            signup ??= AddNewSignup(eventId, userId);
            signup.Status = SignupStatus.SignedUp;
            signup.SignedOffAt = null;

            if (hasSelection)
            {
                signup.WowClass = wowClass;
                signup.WowSpec = wowSpec;
            }

            if (hasPlace)
            {
                signup.SignupOrder = signupOrder;
            }

            await _context.SaveChangesAsync();
            return signupOrder;
        });
    }

    public async Task<bool> SetRosterAsync(ulong eventId, IReadOnlyDictionary<ulong, int> groupsByUser)
    {
        return await RunWithEventLockAsync(eventId, async () =>
        {
            var signups = await _context.EventSignups.Where(s => s.EventId == eventId).ToListAsync();
            // People who answered unsure can be put in a group too: the organiser may want them anyway
            var eligible = signups.Where(s => s.Status is SignupStatus.SignedUp or SignupStatus.Unsure).Select(s => s.UserId).ToHashSet();
            if (groupsByUser.Keys.Any(user => !eligible.Contains(user)))
            {
                return false;
            }

            foreach (var signup in signups)
            {
                signup.GroupNumber = groupsByUser.TryGetValue(signup.UserId, out var group) ? group : null;
            }

            await _context.SaveChangesAsync();
            return true;
        });
    }

    public async Task<bool> SignOffAsync(ulong eventId, ulong userId, bool compactOrder)
    {
        return await RunWithEventLockAsync(eventId, async () =>
        {
            var signup = await GetEventSignupAsync(eventId, userId);
            if (signup == null)
            {
                return false;
            }

            signup.Status = SignupStatus.SignedOff;
            signup.SignedOffAt = DateTime.UtcNow;
            signup.GroupNumber = null;
            await _context.SaveChangesAsync();

            if (compactOrder)
            {
                await CompactOrdersAsync(eventId);
            }

            return true;
        });
    }

    public async Task MarkUnsureAsync(ulong eventId, ulong userId, bool compactOrder)
    {
        await RunWithEventLockAsync(eventId, async () =>
        {
            var signup = await GetEventSignupAsync(eventId, userId) ?? AddNewSignup(eventId, userId);
            signup.Status = SignupStatus.Unsure;
            signup.SignedOffAt = null;
            await _context.SaveChangesAsync();

            if (compactOrder)
            {
                await CompactOrdersAsync(eventId);
            }

            return true;
        });
    }

    private EventSignup AddNewSignup(ulong eventId, ulong userId)
    {
        var signup = new EventSignup
        {
            SignupId = UniqueId.Next(),
            EventId = eventId,
            UserId = userId,
            SignedUpAt = DateTime.UtcNow
        };

        _context.EventSignups.Add(signup);
        return signup;
    }

    /// <summary>Renumbers signed-up people in the line to 1, 2, 3... Call inside the event lock, after the change that opened the gap is saved.</summary>
    private async Task CompactOrdersAsync(ulong eventId)
    {
        var remaining = await _context.EventSignups
            .Where(s => s.EventId == eventId && s.Status == SignupStatus.SignedUp && s.SignupOrder != null)
            .OrderBy(s => s.SignupOrder)
            .ToListAsync();

        for (var i = 0; i < remaining.Count; i++)
        {
            remaining[i].SignupOrder = i + 1;
        }

        await _context.SaveChangesAsync();
    }

    /// <summary>Runs the action under a transaction-scoped Postgres advisory lock keyed on the event.</summary>
    private async Task<T> RunWithEventLockAsync<T>(ulong eventId, Func<Task<T>> action)
    {
        // Retrying database failures (see AddInfrastructure) has to own the transaction, so the whole unit is one retryable step
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({(long)eventId})");
            var result = await action();
            await transaction.CommitAsync();
            return result;
        });
    }
}
