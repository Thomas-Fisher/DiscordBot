using DiscordBot.Application.Interfaces;
using DiscordBot.Domain.Events;
using DiscordBot.Infrastructure.EventBus;
using DiscordBot.Infrastructure.Persistence;
using DiscordBot.Infrastructure.Persistence.Repositories;
using DiscordBot.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Unit.Infrastructure;

public class DuplicateDeliveryTests
{
    private class Recorder
    {
        public List<string> Calls { get; } = new();
    }

    private class OncePerEventHandler : IEventHandler<ReactionAddedEvent>
    {
        private readonly Recorder _recorder;
        public OncePerEventHandler(Recorder recorder) => _recorder = recorder;
        public Task Handle(ReactionAddedEvent @event) { _recorder.Calls.Add("once"); return Task.CompletedTask; }
    }

    [NaturallyIdempotent]
    private class EveryTimeHandler : IEventHandler<ReactionAddedEvent>
    {
        private readonly Recorder _recorder;
        public EveryTimeHandler(Recorder recorder) => _recorder = recorder;
        public Task Handle(ReactionAddedEvent @event) { _recorder.Calls.Add("every"); return Task.CompletedTask; }
    }

    private class FailsThenWorksHandler : IEventHandler<ReactionAddedEvent>
    {
        private readonly Recorder _recorder;
        public FailsThenWorksHandler(Recorder recorder) => _recorder = recorder;

        public Task Handle(ReactionAddedEvent @event)
        {
            _recorder.Calls.Add("attempt");
            var firstAttempt = _recorder.Calls.Count(c => c == "attempt") == 1;
            return firstAttempt ? throw new InvalidOperationException("first time fails") : Task.CompletedTask;
        }
    }

    private static (EventDispatcher Dispatcher, Recorder Recorder, TestDb Db) Create(Action<IServiceCollection> register)
    {
        var db = new TestDb();
        var services = new ServiceCollection();
        var recorder = new Recorder();
        services.AddSingleton(recorder);
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(db.ConnectionString).AddInterceptors(new EnsureGuildsInterceptor()));
        services.AddScoped<IProcessedEventRepository, ProcessedEventRepository>();
        register(services);

        var provider = services.BuildServiceProvider();
        var dispatcher = new EventDispatcher(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<EventDispatcher>.Instance);
        return (dispatcher, recorder, db);
    }

    private static ReactionAddedEvent Reaction() => new() { MessageId = 1, ChannelId = 2, UserId = 3, Emoji = "x" };

    [Fact]
    public async Task Dispatch_SameEventDeliveredTwice_RunsAnOrdinaryHandlerOnlyOnce()
    {
        var (dispatcher, recorder, _) = Create(s => s.AddScoped<IEventHandler<ReactionAddedEvent>, OncePerEventHandler>());
        var @event = Reaction();

        await dispatcher.DispatchAsync(@event);
        await dispatcher.DispatchAsync(@event);

        Assert.Equal(new[] { "once" }, recorder.Calls);
    }

    [Fact]
    public async Task Dispatch_HandlerMarkedNaturallyIdempotent_RunsOnEveryDeliveryAndLeavesNoRecord()
    {
        var (dispatcher, recorder, db) = Create(s => s.AddScoped<IEventHandler<ReactionAddedEvent>, EveryTimeHandler>());
        var @event = Reaction();

        await dispatcher.DispatchAsync(@event);
        await dispatcher.DispatchAsync(@event);

        Assert.Equal(2, recorder.Calls.Count);
        using var context = db.Open();
        Assert.Empty(context.ProcessedEvents);
    }

    [Fact]
    public async Task Dispatch_HandlerFailsThenTheEventIsRedelivered_TheHandlerRunsAgain()
    {
        var (dispatcher, recorder, db) = Create(s => s.AddScoped<IEventHandler<ReactionAddedEvent>, FailsThenWorksHandler>());
        var @event = Reaction();
        await Assert.ThrowsAsync<AggregateException>(() => dispatcher.DispatchAsync(@event));

        await dispatcher.DispatchAsync(@event);

        Assert.Equal(2, recorder.Calls.Count);
        using var context = db.Open();
        Assert.Single(context.ProcessedEvents);
    }

    [Fact]
    public async Task Dispatch_OneOfTwoHandlersFailsThenTheEventIsRedelivered_OnlyTheFailedHandlerRunsAgain()
    {
        var (dispatcher, recorder, _) = Create(s =>
        {
            s.AddScoped<IEventHandler<ReactionAddedEvent>, OncePerEventHandler>();
            s.AddScoped<IEventHandler<ReactionAddedEvent>, FailsThenWorksHandler>();
        });
        var @event = Reaction();
        await Assert.ThrowsAsync<AggregateException>(() => dispatcher.DispatchAsync(@event));

        await dispatcher.DispatchAsync(@event);

        Assert.Equal(1, recorder.Calls.Count(c => c == "once"));
        Assert.Equal(2, recorder.Calls.Count(c => c == "attempt"));
    }
}
