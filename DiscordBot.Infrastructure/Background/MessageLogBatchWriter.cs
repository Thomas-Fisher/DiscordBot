using System.Threading.Channels;
using DiscordBot.Application.Interfaces;
using DiscordBot.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Infrastructure.Background;

/// <summary>Buffers message logs and writes them in batches; a short pause after the first message lets a burst gather into one batch.</summary>
public class MessageLogBatchWriter : BackgroundService, IMessageLogWriter
{
    private const int Capacity = 50_000;
    private const int MaxBatchSize = 500;
    private const int WriteAttempts = 3;
    private static readonly TimeSpan BatchWindow = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DropReportInterval = TimeSpan.FromSeconds(30);

    private readonly Channel<MessageLog> _channel = Channel.CreateBounded<MessageLog>(new BoundedChannelOptions(Capacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropWrite
    });

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MessageLogBatchWriter> _logger;
    private long _dropped;
    private long _lastDropReport;

    public MessageLogBatchWriter(IServiceScopeFactory scopeFactory, ILogger<MessageLogBatchWriter> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public bool Enqueue(MessageLog message)
    {
        if (_channel.Writer.TryWrite(message))
        {
            return true;
        }

        // A full buffer means the database cannot keep up: say so now and then with a count, not once per message
        var dropped = Interlocked.Increment(ref _dropped);
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastDropReport);
        if (now - last > DropReportInterval.TotalMilliseconds && Interlocked.CompareExchange(ref _lastDropReport, now, last) == last)
        {
            _logger.LogWarning("The message log buffer is full: {Dropped} message logs dropped so far", dropped);
        }

        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(stoppingToken))
            {
                await Task.Delay(BatchWindow, stoppingToken);
                await FlushAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down: write whatever is still buffered
        }

        await FlushAsync();
    }

    private async Task FlushAsync()
    {
        while (true)
        {
            var batch = new List<MessageLog>();
            while (batch.Count < MaxBatchSize && _channel.Reader.TryRead(out var message))
            {
                batch.Add(message);
            }

            if (batch.Count == 0)
            {
                return;
            }

            await WriteBatchAsync(batch);
        }
    }

    private async Task WriteBatchAsync(List<MessageLog> batch)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var messageLogs = scope.ServiceProvider.GetRequiredService<IMessageLogRepository>();
                await messageLogs.LogMessagesAsync(batch);
                return;
            }
            catch (Exception ex) when (attempt < WriteAttempts)
            {
                _logger.LogWarning(ex, "Writing a batch of {Count} message logs failed (attempt {Attempt} of {Attempts}); trying again", batch.Count, attempt, WriteAttempts);
                await Task.Delay(RetryDelay * attempt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write a batch of {Count} message logs, giving up on them", batch.Count);
                return;
            }
        }
    }
}
