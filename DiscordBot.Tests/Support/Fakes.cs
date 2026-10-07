using System.Collections.Concurrent;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Events;

namespace DiscordBot.Tests.Support;

public sealed class RecordingMessageUpdater : IEventMessageUpdater
{
    public List<ulong> Refreshed { get; } = new();

    public List<(ulong ChannelId, ulong MessageId)> Removed { get; } = new();

    public Task RequestRemovalAsync(ulong channelId, ulong messageId)
    {
        Removed.Add((channelId, messageId));

        return Task.CompletedTask;
    }

    public Task RequestRefreshAsync(ulong messageId)
    {
        Refreshed.Add(messageId);

        return Task.CompletedTask;
    }
}

public sealed class FakeEventAnnouncer : IEventAnnouncer
{
    public List<(ulong EventId, ulong ChannelId)> Posted { get; } = new();

    /// <summary>When set, posting fails the way it does when the bot may not post in the channel.</summary>
    public bool Fail { get; set; }

    public Task<ulong> PostAsync(Event evt, ulong channelId)
    {
        if (Fail)
        {
            throw new InvalidOperationException("Missing permissions");
        }

        Posted.Add((evt.EventId, channelId));

        return Task.FromResult(evt.EventId + 1);
    }
}

public sealed class FakePingSender : IEventPingSender
{
    public List<(ulong EventId, PingTarget Target, string? Note, IReadOnlyList<PingRecipient> Recipients)> Sent { get; } = new();

    /// <summary>When set, posting fails the way it does when the bot may not write in the channel.</summary>
    public bool Fail { get; set; }

    public Task SendAsync(Event evt, PingTarget target, string? note, IReadOnlyList<PingRecipient> recipients)
    {
        if (Fail)
        {
            throw new InvalidOperationException("Missing permissions");
        }

        Sent.Add((evt.EventId, target, note, recipients));

        return Task.CompletedTask;
    }
}

public sealed record LoggedActivity(string Action, string EntityType, ulong? EntityId, ulong? GuildId, ulong? UserId, string? Source);

public sealed class RecordingActivityLogger : IActivityLogger
{
    public List<LoggedActivity> Entries { get; } = new();

    public Task LogAsync(ActivityEntry entry)
    {
        Entries.Add(new LoggedActivity(entry.Action.ToString(), entry.Entity.ToString(), entry.EntityId, entry.GuildId, entry.UserId, entry.Origin.ToString()));
        return Task.CompletedTask;
    }
}

public sealed class ImmediateTaskQueue : IBackgroundTaskQueue
{
    public bool Enqueue(string name, Func<CancellationToken, Task> work)
    {
        // Enqueue is synchronous, and a test wants the work finished when it returns, so this waits for it
        work(CancellationToken.None).GetAwaiter().GetResult();
        return true;
    }
}

public sealed class RecordingEventBus : IEventBus
{
    public List<IDomainEvent> Published { get; } = new();

    public Task PublishAsync(IDomainEvent @event)
    {
        Published.Add(@event);
        return Task.CompletedTask;
    }
}

public sealed class RecordingMessageSender : IDiscordMessageSender
{
    public List<(ulong ChannelId, string Content)> Sent { get; } = new();

    public Task SendMessageAsync(ulong channelId, string content)
    {
        Sent.Add((channelId, content));
        return Task.CompletedTask;
    }
}

public sealed class RecordingMessageLogWriter : IMessageLogWriter
{
    private readonly ConcurrentQueue<MessageLog> _queued = new();

    public IReadOnlyCollection<MessageLog> Queued => _queued.ToArray();

    public bool Enqueue(MessageLog message)
    {
        _queued.Enqueue(message);
        return true;
    }
}

public sealed class FixedTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    public DateTime UtcNow => Now.UtcDateTime;

    public void Advance(TimeSpan by) => Now += by;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>A Discord server list the tests fill in. With nothing added the bot is "in" no server.</summary>
public sealed class FakeGuildDirectory : IGuildDirectory
{
    private readonly Dictionary<ulong, (GuildInfo Info, List<GuildChannel> Channels, List<GuildRole> Roles)> _guilds = new();

    public FakeGuildDirectory Add(GuildInfo info, IEnumerable<GuildChannel>? channels = null, IEnumerable<GuildRole>? roles = null)
    {
        _guilds[info.Id] = (info, (channels ?? []).ToList(), (roles ?? []).ToList());
        return this;
    }

    public HashSet<(ulong GuildId, ulong UserId)> Members { get; } = new();

    /// <summary>What the guild shows for a member. A member with no entry is treated as not being in the server.</summary>
    public Dictionary<(ulong GuildId, ulong UserId), string> Names { get; } = new();

    public Dictionary<(ulong GuildId, ulong RoleId), HashSet<ulong>> RoleMembers { get; } = new();

    public Task<IReadOnlyCollection<ulong>?> GetRoleMemberIdsAsync(ulong guildId, IReadOnlyCollection<ulong> roleIds, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ulong>? members = _guilds.ContainsKey(guildId)
            ? roleIds.SelectMany(role => RoleMembers.TryGetValue((guildId, role), out var ids) ? ids : []).Distinct().ToList()
            : null;

        return Task.FromResult(members);
    }

    public Dictionary<ulong, List<GuildMember>> MemberList { get; } = new();

    public Task<IReadOnlyList<GuildMember>?> SearchMembersAsync(ulong guildId, string? search, int limit, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GuildMember>? found = _guilds.ContainsKey(guildId)
            ? (MemberList.TryGetValue(guildId, out var all) ? all : [])
                .Where(m => string.IsNullOrWhiteSpace(search) || m.DisplayName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) || m.Username.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToList()
            : null;

        return Task.FromResult(found);
    }

    public Task<IReadOnlyDictionary<ulong, string>> GetMemberNamesAsync(ulong guildId, IEnumerable<ulong> userIds, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<ulong, string> names = userIds.Distinct().Where(id => Names.ContainsKey((guildId, id))).ToDictionary(id => id, id => Names[(guildId, id)]);

        return Task.FromResult(names);
    }

    public Task<bool> IsMemberAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default) => Task.FromResult(Members.Contains((guildId, userId)));

    public Task<GuildInfo?> FindAsync(ulong guildId, CancellationToken cancellationToken = default) => Task.FromResult(_guilds.TryGetValue(guildId, out var g) ? g.Info : null);

    public Task<IReadOnlyList<GuildChannel>?> GetTextChannelsAsync(ulong guildId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GuildChannel>?>(_guilds.TryGetValue(guildId, out var g) ? g.Channels : null);

    public Task<IReadOnlyList<GuildRole>?> GetPingableRolesAsync(ulong guildId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GuildRole>?>(_guilds.TryGetValue(guildId, out var g) ? g.Roles : null);
}
