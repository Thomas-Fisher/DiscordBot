using DiscordBot.Domain.Common;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DiscordBot.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Guild> Guilds { get; set; } = null!;
    public DbSet<GuildSettings> GuildSettings { get; set; } = null!;
    public DbSet<MessageLog> MessageLogs { get; set; } = null!;
    public DbSet<Event> Events { get; set; } = null!;
    public DbSet<EventSignup> EventSignups { get; set; } = null!;
    public DbSet<RememberedSpec> RememberedSpecs { get; set; } = null!;
    public DbSet<ActivityLog> ActivityLogs { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<ProcessedEvent> ProcessedEvents { get; set; } = null!;
    public DbSet<OutboxMessage> Outbox { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Guild>()
            .HasKey(g => g.GuildId);

        // Column limits mirror the rules the application enforces (EventRules, Discord's own limits), so a write that skips the
        // application cannot get past them either
        modelBuilder.Entity<Guild>()
            .Property(g => g.GuildName)
            .HasMaxLength(100);

        modelBuilder.Entity<GuildSettings>()
            .Property(s => s.SettingKey)
            .HasMaxLength(100);

        modelBuilder.Entity<GuildSettings>()
            .Property(s => s.SettingValue)
            .HasMaxLength(1000);

        modelBuilder.Entity<MessageLog>()
            .Property(m => m.Content)
            .HasMaxLength(4000);

        modelBuilder.Entity<Event>()
            .Property(e => e.Title)
            .HasMaxLength(EventRules.MaxTitleLength);

        modelBuilder.Entity<Event>()
            .Property(e => e.Description)
            .HasMaxLength(EventRules.MaxDescriptionLength);

        // Enums are stored by name, so reordering or inserting a member cannot silently change what old rows mean (or what an index's
        // filter selects)
        modelBuilder.Entity<Event>().Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
        modelBuilder.Entity<Event>().Property(e => e.Type).HasConversion<string>().HasMaxLength(32);
        modelBuilder.Entity<Event>().Property(e => e.RecurrencePattern).HasConversion<string>().HasMaxLength(32);
        modelBuilder.Entity<EventSignup>().Property(s => s.Status).HasConversion<string>().HasMaxLength(32);

        modelBuilder.Entity<EventSignup>().Property(s => s.WowClass).HasMaxLength(32);
        modelBuilder.Entity<EventSignup>().Property(s => s.WowSpec).HasMaxLength(32);

        modelBuilder.Entity<ActivityLog>().Property(a => a.ActionType).HasMaxLength(64);
        modelBuilder.Entity<ActivityLog>().Property(a => a.EntityType).HasMaxLength(64);
        modelBuilder.Entity<ActivityLog>().Property(a => a.Source).HasMaxLength(32);

        modelBuilder.Entity<RefreshToken>().Property(t => t.TokenHash).HasMaxLength(64);
        modelBuilder.Entity<RefreshToken>().Property(t => t.Username).HasMaxLength(100);
        modelBuilder.Entity<RefreshToken>().Property(t => t.Avatar).HasMaxLength(100);

        modelBuilder.Entity<OutboxMessage>().HasKey(o => o.Id);
        modelBuilder.Entity<OutboxMessage>().Property(o => o.Type).HasMaxLength(200);
        modelBuilder.Entity<OutboxMessage>().Property(o => o.PartitionKey).HasMaxLength(200);

        // The relay only ever looks at what is still waiting, oldest first
        modelBuilder.Entity<OutboxMessage>()
            .HasIndex(o => o.CreatedAt)
            .HasDatabaseName("IX_Outbox_Unpublished")
            .HasFilter("\"PublishedAt\" IS NULL");

        modelBuilder.Entity<OutboxMessage>()
            .HasIndex(o => o.PublishedAt);

        modelBuilder.Entity<Guild>()
            .HasMany(g => g.Settings)
            .WithOne(s => s.Guild)
            .HasForeignKey(s => s.GuildId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Guild>()
            .HasMany(g => g.MessageLogs)
            .WithOne(m => m.Guild)
            .HasForeignKey(m => m.GuildId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GuildSettings>()
            .HasKey(s => s.SettingId);

        modelBuilder.Entity<GuildSettings>()
            .HasIndex(s => new { s.GuildId, s.SettingKey })
            .IsUnique();

        modelBuilder.Entity<MessageLog>()
            .HasKey(m => m.MessageId);

        modelBuilder.Entity<MessageLog>()
            .HasIndex(m => new { m.GuildId, m.LoggedAt })
            .IsDescending(false, true);

        modelBuilder.Entity<MessageLog>()
            .HasIndex(m => m.LoggedAt);

        modelBuilder.Entity<Event>()
            .HasKey(e => e.EventId);

        // Role ids are kept as one comma-separated text column ("1,2,3"); it is only ever read together with its event
        modelBuilder.Entity<Event>()
            .Property(e => e.MentionRoleIds)
            .HasConversion(
                ids => string.Join(',', ids),
                text => ParseRoleIds(text),
                new ValueComparer<List<ulong>>(
                    (a, b) => a!.SequenceEqual(b!),
                    ids => ids.Aggregate(0, (hash, id) => HashCode.Combine(hash, id)),
                    ids => ids.ToList()));

        // The two fields the background workers act on: checking them in the UPDATE makes "claim this reminder" and "roll this series
        // forward" succeed for one worker only, however many are running
        modelBuilder.Entity<Event>()
            .Property(e => e.ReminderSent)
            .IsConcurrencyToken();

        modelBuilder.Entity<Event>()
            .Property(e => e.Status)
            .IsConcurrencyToken();

        modelBuilder.Entity<Event>()
            .HasIndex(e => new { e.GuildId, e.ScheduledTime });

        // Every reaction on a message looks the event up by message id
        modelBuilder.Entity<Event>()
            .HasIndex(e => e.MessageId);

        // The reminder / recurrence workers only ever look at live, scheduled events ordered by time
        modelBuilder.Entity<Event>()
            .HasIndex(e => e.ScheduledTime)
            .HasDatabaseName("IX_Events_ScheduledTime_Active")
            .HasFilter($"\"IsDeleted\" = false AND \"Status\" = '{nameof(EventStatus.Scheduled)}'");

        modelBuilder.Entity<Event>()
            .HasMany(e => e.Signups)
            .WithOne(s => s.Event)
            .HasForeignKey(s => s.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<EventSignup>()
            .HasKey(s => s.SignupId);

        modelBuilder.Entity<EventSignup>()
            .HasIndex(s => new { s.EventId, s.UserId })
            .IsUnique();

        modelBuilder.Entity<EventSignup>()
            .HasIndex(s => new { s.EventId, s.Status });

        modelBuilder.Entity<RememberedSpec>()
            .HasKey(r => new { r.GuildId, r.UserId, r.EventType, r.WowClass });

        modelBuilder.Entity<RememberedSpec>().Property(r => r.EventType).HasConversion<string>().HasMaxLength(32);
        modelBuilder.Entity<RememberedSpec>().Property(r => r.WowClass).HasMaxLength(32);
        modelBuilder.Entity<RememberedSpec>().Property(r => r.WowSpec).HasMaxLength(32);

        modelBuilder.Entity<ActivityLog>()
            .HasKey(a => a.LogId);

        // Keyset paging orders by (Timestamp desc, LogId desc) within a guild, optionally filtered by action type
        modelBuilder.Entity<ActivityLog>()
            .HasIndex(a => new { a.GuildId, a.Timestamp, a.LogId })
            .IsDescending(false, true, true);

        modelBuilder.Entity<ActivityLog>()
            .HasIndex(a => new { a.GuildId, a.ActionType, a.Timestamp, a.LogId })
            .IsDescending(false, false, true, true);

        // Retention deletes by age across all guilds
        modelBuilder.Entity<ActivityLog>()
            .HasIndex(a => a.Timestamp);

        modelBuilder.Entity<RefreshToken>()
            .HasKey(t => t.Id);

        // Rotating a token revokes it; if two requests try that at once, only the first save may succeed
        modelBuilder.Entity<RefreshToken>()
            .Property(t => t.RevokedAt)
            .IsConcurrencyToken();

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(t => t.TokenHash)
            .IsUnique();

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(t => t.FamilyId);

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(t => t.ExpiresAt);

        modelBuilder.Entity<ProcessedEvent>()
            .HasKey(p => new { p.EventId, p.Handler });

        modelBuilder.Entity<ProcessedEvent>()
            .Property(p => p.Handler)
            .HasMaxLength(200);

        modelBuilder.Entity<ProcessedEvent>()
            .HasIndex(p => p.ProcessedAt);
    }

    // A value that is not a number is dropped rather than making the event impossible to load
    private static List<ulong> ParseRoleIds(string text)
    {
        return text.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => ulong.TryParse(part, out var id) ? id : 0UL)
            .Where(id => id != 0)
            .ToList();
    }
}
