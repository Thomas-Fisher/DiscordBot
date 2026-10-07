using System.Diagnostics;
using DiscordBot.Application.Telemetry;

namespace DiscordBot.Tests.Support;

/// <summary>Listens to the bot's traces for the length of a test, so tests can check which spans were started and how they are linked.</summary>
public sealed class SpanRecorder : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _stopped = new();
    private readonly object _lock = new();

    public SpanRecorder()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BotTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_lock)
                {
                    _stopped.Add(activity);
                }
            }
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public List<Activity> Spans
    {
        get
        {
            lock (_lock)
            {
                return _stopped.ToList();
            }
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
    }
}
