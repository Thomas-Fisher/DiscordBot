using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Telemetry;
using DiscordBot.Domain.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Infrastructure.EventBus;

public class EventDispatcher : IEventDispatcher
{
    // What is worked out once per type instead of once per event: the handler interface and a direct call to its Handle method, and
    // whether a handler is exempt from the duplicate-delivery record
    private static readonly ConcurrentDictionary<Type, (Type HandlerType, Func<object, IDomainEvent, Task> Handle)> HandlerInfo = new();
    private static readonly ConcurrentDictionary<Type, bool> NaturallyIdempotent = new();
    private static readonly MethodInfo CallHandleMethod = typeof(EventDispatcher).GetMethod(nameof(CallHandle), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly Histogram<double> HandlerDuration = BotTelemetry.Meter.CreateHistogram<double>("discordbot.handler.duration", "ms", "How long an event handler took");
    private static readonly Counter<long> HandlerFailures = BotTelemetry.Meter.CreateCounter<long>("discordbot.handler.failures", description: "Event handler runs that threw");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventDispatcher> _logger;

    public EventDispatcher(IServiceScopeFactory scopeFactory, ILogger<EventDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task DispatchAsync(IDomainEvent @event)
    {
        var eventType = @event.GetType();
        var (handlerType, handle) = HandlerInfo.GetOrAdd(eventType, type =>
            (typeof(IEventHandler<>).MakeGenericType(type), CallHandleMethod.MakeGenericMethod(type).CreateDelegate<Func<object, IDomainEvent, Task>>()));

        // A fresh scope per event: scoped services a handler resolves live exactly as long as this dispatch
        using var scope = _scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices(handlerType).Where(h => h != null).Cast<object>().ToList();

        if (handlers.Count == 0)
        {
            _logger.LogDebug("No handlers registered for {EventType}", eventType.Name);
            return;
        }

        // One after another, not in parallel: the handlers share this scope, and with it one database context, which
        // cannot be used by two handlers at the same moment
        var failures = new List<Exception>();
        foreach (var handler in handlers)
        {
            var failure = await RunHandlerAsync(scope.ServiceProvider, handler, handle, @event);
            if (failure != null)
            {
                failures.Add(failure);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException($"{failures.Count} of {handlers.Count} handlers failed for {eventType.Name} {@event.EventId}", failures);
        }
    }

    private async Task<Exception?> RunHandlerAsync(IServiceProvider services, object handler, Func<object, IDomainEvent, Task> handle, IDomainEvent @event)
    {
        var handlerName = handler.GetType().Name;
        using var activity = BotTelemetry.Source.StartActivity($"{handlerName} handle");
        activity?.SetTag("discordbot.handler", handlerName);
        activity?.SetTag("discordbot.event.type", @event.GetType().Name);
        var started = Stopwatch.GetTimestamp();

        try
        {
            var naturallyIdempotent = NaturallyIdempotent.GetOrAdd(handler.GetType(), type => type.IsDefined(typeof(NaturallyIdempotentAttribute), inherit: false));
            var processed = naturallyIdempotent ? null : services.GetRequiredService<IProcessedEventRepository>();

            // A broker can deliver an event twice, and a retry runs every handler again; a handler that already succeeded is skipped
            if (processed != null && await processed.HasProcessedAsync(@event.EventId, handlerName))
            {
                _logger.LogInformation("Skipping duplicate delivery of {EventType} {EventId} to {Handler}", @event.GetType().Name, @event.EventId, handlerName);
                return null;
            }

            await handle(handler, @event);

            if (processed != null)
            {
                await processed.MarkProcessedAsync(@event.EventId, handlerName);
            }

            return null;
        }
        catch (Exception ex)
        {
            return LogFailure(handler, @event, ex, activity);
        }
        finally
        {
            HandlerDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, new KeyValuePair<string, object?>("handler", handlerName));
        }
    }

    private Exception LogFailure(object handler, IDomainEvent @event, Exception exception, Activity? activity)
    {
        activity?.AddException(exception);
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        HandlerFailures.Add(1, new KeyValuePair<string, object?>("handler", handler.GetType().Name));
        _logger.LogError(exception, "Handler {Handler} failed for {EventType} {EventId}", handler.GetType().Name, @event.GetType().Name, @event.EventId);
        return exception;
    }

    private static Task CallHandle<TEvent>(object handler, IDomainEvent @event)
        where TEvent : IDomainEvent
    {
        return ((IEventHandler<TEvent>)handler).Handle((TEvent)@event);
    }
}
