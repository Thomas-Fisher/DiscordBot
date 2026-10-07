using DiscordBot.Application.Interfaces;
using DiscordBot.Domain.Entities;
using DiscordBot.Infrastructure.Background;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using DiscordBot.Tests.Support;

namespace DiscordBot.Tests.Unit.Infrastructure;

public class MessageLogBatchWriterTests
{
    private sealed class BatchCollectingRepository : IMessageLogRepository
    {
        private readonly object _lock = new();
        private bool _failNextWrite;

        public BatchCollectingRepository(bool failFirstWrite = false)
        {
            _failNextWrite = failFirstWrite;
        }

        public List<List<MessageLog>> Batches { get; } = new();

        public TaskCompletionSource FirstWriteFailed { get; } = new();

        public int StoredCount
        {
            get
            {
                lock (_lock)
                {
                    return Batches.Sum(b => b.Count);
                }
            }
        }

        public Task LogMessagesAsync(IReadOnlyCollection<MessageLog> messages)
        {
            lock (_lock)
            {
                if (_failNextWrite)
                {
                    _failNextWrite = false;
                    FirstWriteFailed.SetResult();
                    throw new InvalidOperationException("db down");
                }

                Batches.Add(messages.ToList());
            }

            return Task.CompletedTask;
        }

        public Task<MessageLog?> FindAsync(ulong messageId) => throw new NotSupportedException();

        public Task MarkDeletedAsync(ulong messageId) => throw new NotSupportedException();

        public Task DeleteOldMessagesAsync(int retentionDays) => throw new NotSupportedException();
    }

    private static MessageLogBatchWriter CreateWriter(BatchCollectingRepository repository)
    {
        var provider = new ServiceCollection().AddSingleton<IMessageLogRepository>(repository).BuildServiceProvider();
        return new MessageLogBatchWriter(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<MessageLogBatchWriter>.Instance);
    }

    private static MessageLog Message(ulong id) => new() { MessageId = id, GuildId = 1, ChannelId = 2, AuthorId = 3, Content = "hi" };

    private static async Task WaitForStoredAsync(BatchCollectingRepository repository, int count)
    {
        await Eventually.UntilAsync(() => repository.StoredCount >= count, $"{count} stored messages");
    }

    [Fact]
    public async Task Enqueue_BurstOfMessages_IsStoredInOrderAndBatchedRatherThanWrittenOneByOne()
    {
        var repository = new BatchCollectingRepository();
        var writer = CreateWriter(repository);
        await writer.StartAsync(CancellationToken.None);

        for (ulong id = 1; id <= 100; id++)
        {
            writer.Enqueue(Message(id));
        }

        await WaitForStoredAsync(repository, 100);
        await writer.StopAsync(CancellationToken.None);

        Assert.True(repository.Batches.Count < 50, $"expected batching but got {repository.Batches.Count} batches for 100 messages");
        Assert.Equal(Enumerable.Range(1, 100).Select(i => (ulong)i), repository.Batches.SelectMany(b => b).Select(m => m.MessageId));
    }

    [Fact]
    public async Task Enqueue_MoreThanTheMaximumBatchSize_IsSplitIntoBoundedBatches()
    {
        var repository = new BatchCollectingRepository();
        var writer = CreateWriter(repository);
        for (ulong id = 1; id <= 1200; id++)
        {
            writer.Enqueue(Message(id));
        }

        await writer.StartAsync(CancellationToken.None);
        await WaitForStoredAsync(repository, 1200);
        await writer.StopAsync(CancellationToken.None);

        Assert.All(repository.Batches, batch => Assert.True(batch.Count <= 500));
    }

    [Fact]
    public async Task Enqueue_BatchThatFailedOnceToStore_IsTriedAgainAndLaterMessagesAreStoredToo()
    {
        var repository = new BatchCollectingRepository(failFirstWrite: true);
        var writer = CreateWriter(repository);
        await writer.StartAsync(CancellationToken.None);
        writer.Enqueue(Message(1));
        await repository.FirstWriteFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        writer.Enqueue(Message(2));
        await WaitForStoredAsync(repository, 2);
        await writer.StopAsync(CancellationToken.None);

        // Nothing was lost to the failure
        Assert.Equal(new ulong[] { 1, 2 }, repository.Batches.SelectMany(b => b).Select(m => m.MessageId).Order());
    }

    [Fact]
    public async Task StopAsync_MessagesStillBuffered_AreStoredBeforeStopping()
    {
        var repository = new BatchCollectingRepository();
        var writer = CreateWriter(repository);
        await writer.StartAsync(CancellationToken.None);
        writer.Enqueue(Message(1));
        await WaitForStoredAsync(repository, 1);
        writer.Enqueue(Message(2));
        writer.Enqueue(Message(3));

        await writer.StopAsync(CancellationToken.None);

        Assert.Equal(3, repository.StoredCount);
    }
}
