using DiscordBot.Application.Interfaces;
using DiscordBot.Domain.Events;
using DiscordBot.Infrastructure.EventBus;
using DiscordBot.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Unit.Infrastructure;

public class EventDispatcherTests
{
    private class Recorder
    {
        public List<string> Calls { get; } = new();
    }

    private class ScopedThing
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private class ReactionHandlerA : IEventHandler<ReactionAddedEvent>
    {
        private readonly Recorder _recorder;
        public ReactionHandlerA(Recorder recorder) => _recorder = recorder;
        public Task Handle(ReactionAddedEvent @event) { _recorder.Calls.Add("A:" + @event.Emoji); return Task.CompletedTask; }
    }

    private class ReactionHandlerB : IEventHandler<ReactionAddedEvent>
    {
        private readonly Recorder _recorder;
        public ReactionHandlerB(Recorder recorder) => _recorder = recorder;
        public Task Handle(ReactionAddedEvent @event) { _recorder.Calls.Add("B:" + @event.Emoji); return Task.CompletedTask; }
    }

    private class FailingHandler : IEventHandler<ReactionAddedEvent>
    {
        public Task Handle(ReactionAddedEvent @event) => throw new InvalidOperationException("handler blew up");
    }

    private class AsyncFailingHandler : IEventHandler<ReactionAddedEvent>
    {
        public async Task Handle(ReactionAddedEvent @event)
        {
            await Task.Yield();
            throw new InvalidOperationException("async blew up");
        }
    }

    private class MessageHandler : IEventHandler<MessageReceivedEvent>
    {
        private readonly Recorder _recorder;
        public MessageHandler(Recorder recorder) => _recorder = recorder;
        public Task Handle(MessageReceivedEvent @event) { _recorder.Calls.Add("M"); return Task.CompletedTask; }
    }

    [NaturallyIdempotent]
    private class ScopeProbe : IEventHandler<ReactionAddedEvent>
    {
        private readonly ScopedThing _scoped;
        private readonly Recorder _recorder;
        public ScopeProbe(ScopedThing scoped, Recorder recorder) { _scoped = scoped; _recorder = recorder; }
        public Task Handle(ReactionAddedEvent @event) { _recorder.Calls.Add(_scoped.Id.ToString()); return Task.CompletedTask; }
    }

    private class OverlapCounter
    {
        private int _running;
        private int _mostRunningAtOnce;

        public int MostRunningAtOnce => Volatile.Read(ref _mostRunningAtOnce);

        public void Enter()
        {
            var now = Interlocked.Increment(ref _running);
            int seen;
            do
            {
                seen = Volatile.Read(ref _mostRunningAtOnce);
            }
            while (now > seen && Interlocked.CompareExchange(ref _mostRunningAtOnce, now, seen) != seen);
        }

        public void Leave() => Interlocked.Decrement(ref _running);
    }

    private class OverlapProbe : IEventHandler<ReactionAddedEvent>
    {
        private readonly OverlapCounter _counter;

        public OverlapProbe(OverlapCounter counter) => _counter = counter;

        public async Task Handle(ReactionAddedEvent @event)
        {
            _counter.Enter();
            await Task.Delay(30);
            _counter.Leave();
        }
    }

    private class SecondOverlapProbe : OverlapProbe
    {
        public SecondOverlapProbe(OverlapCounter counter) : base(counter)
        {
        }
    }

    private static (EventDispatcher Dispatcher, Recorder Recorder) Create(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        var recorder = new Recorder();
        services.AddSingleton(recorder);
        services.AddScoped<ScopedThing>();

        // Handlers are run once per event unless they are naturally idempotent, which needs somewhere to record what has run
        var db = new TestDb();
        services.AddDbContext<DiscordBot.Infrastructure.Persistence.AppDbContext>(o => o.UseNpgsql(db.ConnectionString));
        services.AddScoped<IProcessedEventRepository, DiscordBot.Infrastructure.Persistence.Repositories.ProcessedEventRepository>();
        register(services);

        var provider = services.BuildServiceProvider();
        return (new EventDispatcher(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<EventDispatcher>.Instance), recorder);
    }

    private static ReactionAddedEvent Reaction(string emoji = "x") => new() { MessageId = 1, ChannelId = 2, UserId = 3, Emoji = emoji };

    [Fact]
    public async Task Dispatch_EventWithSeveralHandlers_RunsEveryHandlerOfThatEventTypeOnly()
    {
        var (dispatcher, recorder) = Create(s =>
        {
            s.AddSingleton<IEventHandler<ReactionAddedEvent>, ReactionHandlerA>();
            s.AddSingleton<IEventHandler<ReactionAddedEvent>, ReactionHandlerB>();
            s.AddSingleton<IEventHandler<MessageReceivedEvent>, MessageHandler>();
        });

        await dispatcher.DispatchAsync(Reaction("👍"));

        Assert.Equivalent(new[] { "A:👍", "B:👍" }, recorder.Calls);
    }

    [Theory]
    [InlineData(typeof(FailingHandler), "handler blew up")]
    [InlineData(typeof(AsyncFailingHandler), "async blew up")]
    public async Task Dispatch_OneHandlerFails_OthersStillRunAndTheOriginalFailureIsReported(Type failingHandler, string expectedMessage)
    {
        var (dispatcher, recorder) = Create(s =>
        {
            s.AddSingleton<IEventHandler<ReactionAddedEvent>, ReactionHandlerA>();
            s.AddSingleton(typeof(IEventHandler<ReactionAddedEvent>), failingHandler);
            s.AddSingleton<IEventHandler<ReactionAddedEvent>, ReactionHandlerB>();
        });

        var failure = await Assert.ThrowsAsync<AggregateException>(() => dispatcher.DispatchAsync(Reaction("z")));

        Assert.Equivalent(new[] { "A:z", "B:z" }, recorder.Calls);
        Assert.Equal(expectedMessage, Assert.Single(failure.InnerExceptions).Message);
    }

    [Fact]
    public async Task Dispatch_TwoEvents_EachGetsItsOwnScope()
    {
        var (dispatcher, recorder) = Create(s => s.AddTransient<IEventHandler<ReactionAddedEvent>, ScopeProbe>());

        await dispatcher.DispatchAsync(Reaction());
        await dispatcher.DispatchAsync(Reaction());

        Assert.Equal(2, recorder.Calls.Distinct().Count());
    }

    [Fact]
    public async Task Dispatch_SeveralHandlersOfOneEvent_ShareOneScope()
    {
        var (dispatcher, recorder) = Create(s =>
        {
            s.AddTransient<IEventHandler<ReactionAddedEvent>, ScopeProbe>();
            s.AddTransient<IEventHandler<ReactionAddedEvent>, ScopeProbe>();
        });

        await dispatcher.DispatchAsync(Reaction());

        Assert.Equal(2, recorder.Calls.Count);
        Assert.Single(recorder.Calls.Distinct());
    }

    [Fact]
    public async Task Dispatch_SeveralHandlersOfOneEvent_RunOneAfterAnotherBecauseTheyShareADatabaseContext()
    {
        var counter = new OverlapCounter();
        var (dispatcher, _) = Create(s =>
        {
            s.AddSingleton(counter);
            s.AddScoped<IEventHandler<ReactionAddedEvent>, OverlapProbe>();
            s.AddScoped<IEventHandler<ReactionAddedEvent>, SecondOverlapProbe>();
        });

        await dispatcher.DispatchAsync(Reaction());

        Assert.Equal(1, counter.MostRunningAtOnce);
    }
}
