using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Application.Options;
using DiscordBot.Application.Services;
using DiscordBot.Discord;
using DiscordBot.Discord.BackgroundServices;
using DiscordBot.Domain.Entities;
using DiscordBot.Infrastructure.Persistence;
using DiscordBot.Infrastructure.Persistence.Repositories;
using DiscordBot.Tests.Support;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DiscordBot.Tests.Unit.Discord;

/// <summary>
/// The reminder worker. Which reminders are due is <see cref="EventScheduler"/>'s job (tested there) and who is pinged is
/// <see cref="EventPingService"/>'s (tested there); this checks that the two are put together: the people in the event's roles who
/// have not answered are pinged, and a reminder that cannot be sent is not sent again.
/// </summary>
public class EventReminderServiceTests : IDisposable
{
    private const ulong Role = 11;

    private readonly FixedTimeProvider _clock = new();
    private readonly TestDb _db;
    private readonly FakeGuildDirectory _directory = new();
    private readonly FakePingSender _sender = new();
    private readonly EventReminderService _service;
    private readonly DiscordSocketClient _client = new();

    public EventReminderServiceTests()
    {
        _db = new TestDb(_clock);
        _directory.Add(TestGuilds.Basic(TestEvents.GuildId), roles: [new GuildRole(Role, "Raiders", 1)]);
        _directory.RoleMembers[(TestEvents.GuildId, Role)] = new HashSet<ulong> { 1, 2, 3 };

        var services = new ServiceCollection();
        services.AddScoped(_ => _db.Open());
        services.AddScoped<IEventRepository>(sp => new EventRepository(sp.GetRequiredService<AppDbContext>(), _clock));
        services.AddScoped<ISignupRepository>(sp => new SignupRepository(sp.GetRequiredService<AppDbContext>()));
        services.AddScoped<ISettingsService>(sp => new SettingsService(new GuildRepository(sp.GetRequiredService<AppDbContext>()), new MemoryCache(new MemoryCacheOptions()), NullLogger<SettingsService>.Instance));
        services.AddSingleton<IActivityLogger>(new RecordingActivityLogger());
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<IGuildDirectory>(_directory);
        services.AddSingleton<IEventPingSender>(_sender);
        services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
        services.AddScoped<IEventScheduler, EventScheduler>();
        services.AddScoped<IEventPingService, EventPingService>();
        services.AddLogging();

        _service = new EventReminderService(services.BuildServiceProvider(), new DiscordConnection(_client), NullLogger<EventReminderService>.Instance,
            Options.Create(new EventReminderOptions()));
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private async Task StoreDueEventAsync(Action<Event>? change = null)
    {
        var evt = TestEvents.Wow(1);
        evt.ScheduledTime = _clock.UtcNow.AddMinutes(20);
        evt.ReminderMinutesBefore = 30;
        evt.ReminderSent = false;
        evt.MentionRoleIds = [Role];
        change?.Invoke(evt);
        _db.Context.Events.Add(evt);
        await _db.Context.SaveChangesAsync();
    }

    private async Task<bool> ReminderSentAsync()
    {
        await using var context = _db.Open();

        return (await context.Events.AsNoTracking().SingleAsync(e => e.EventId == 1)).ReminderSent;
    }

    [Fact]
    public async Task SendDueReminders_EventWithPingedRoles_PingsTheMembersWhoHaveNotAnsweredAndClaimsTheReminder()
    {
        await StoreDueEventAsync();
        await _db.Signups.SignUpAsync(1, 1, "mage", "fire");

        await _service.SendDueRemindersAsync();

        var sent = Assert.Single(_sender.Sent);
        Assert.Equal(PingTarget.NoResponse, sent.Target);
        Assert.Equal(new ulong[] { 2, 3 }, sent.Recipients.Select(r => r.UserId));
        Assert.True(await ReminderSentAsync());
    }

    [Fact]
    public async Task SendDueReminders_EventWithNoPingedRoles_HasNobodyToLookForAndSendsNothing()
    {
        await StoreDueEventAsync(e => e.MentionRoleIds = []);

        var failure = await Record.ExceptionAsync(() => _service.SendDueRemindersAsync());

        Assert.Null(failure);
        Assert.Empty(_sender.Sent);
        Assert.True(await ReminderSentAsync());
    }

    [Fact]
    public async Task SendDueReminders_EveryoneHasAlreadyAnswered_SendsNothing()
    {
        await StoreDueEventAsync();
        foreach (ulong user in new ulong[] { 1, 2, 3 })
        {
            await _db.Signups.SignUpAsync(1, user, "mage", "fire");
        }

        await _service.SendDueRemindersAsync();

        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task SendDueReminders_EventWithNoReminderSet_IsLeftAlone()
    {
        await StoreDueEventAsync(e => e.ReminderMinutesBefore = null);

        await _service.SendDueRemindersAsync();

        Assert.Empty(_sender.Sent);
        Assert.False(await ReminderSentAsync());
    }

    [Fact]
    public async Task SendDueReminders_PostingFails_DoesNotUndoTheClaim()
    {
        await StoreDueEventAsync();
        _sender.Fail = true;

        var failure = await Record.ExceptionAsync(() => _service.SendDueRemindersAsync());

        Assert.Null(failure);
        Assert.True(await ReminderSentAsync());
    }
}
