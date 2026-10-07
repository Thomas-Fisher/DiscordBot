using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Options;
using DiscordBot.Infrastructure.Background;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace DiscordBot.Tests.Unit.Infrastructure;

public class DataRetentionServiceTests
{
    private readonly Dictionary<string, int> _cleared = new();
    private readonly Mock<IMessageLogRepository> _messageLogs = new();
    private readonly Mock<IActivityLogRepository> _activityLogs = new();
    private readonly Mock<IProcessedEventRepository> _processedEvents = new();

    public DataRetentionServiceTests()
    {
        _messageLogs.Setup(x => x.DeleteOldMessagesAsync(It.IsAny<int>())).Callback<int>(days => _cleared["messages"] = days).Returns(Task.CompletedTask);
        _activityLogs.Setup(x => x.DeleteOldActivityLogsAsync(It.IsAny<int>())).Callback<int>(days => _cleared["activity"] = days).Returns(Task.CompletedTask);
        _processedEvents.Setup(x => x.DeleteOlderThanAsync(It.IsAny<int>())).Callback<int>(days => _cleared["processed"] = days).Returns(Task.CompletedTask);
    }

    private DataRetentionService Create(RetentionOptions options)
    {
        var provider = new ServiceCollection()
            .AddSingleton(_messageLogs.Object)
            .AddSingleton(_activityLogs.Object)
            .AddSingleton(_processedEvents.Object)
            .BuildServiceProvider();

        return new DataRetentionService(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(options), NullLogger<DataRetentionService>.Instance);
    }

    [Fact]
    public async Task RunOnce_ConfiguredPeriods_ClearsEachStoreWithItsOwnNumberOfDays()
    {
        var service = Create(new RetentionOptions { MessageLogDays = 14, ActivityLogDays = 60, ProcessedEventDays = 3 });

        await service.RunOnceAsync();

        Assert.Equal(new Dictionary<string, int> { ["messages"] = 14, ["activity"] = 60, ["processed"] = 3 }, _cleared);
    }

    [Fact]
    public async Task RunOnce_ZeroDaysForAStore_KeepsThatStoreForever()
    {
        var service = Create(new RetentionOptions { MessageLogDays = 0, ActivityLogDays = 60, ProcessedEventDays = 7 });

        await service.RunOnceAsync();

        Assert.DoesNotContain("messages", _cleared.Keys);
        Assert.Equal(60, _cleared["activity"]);
    }

    [Fact]
    public async Task RunOnce_OneStoreFails_DoesNotThrow()
    {
        _messageLogs.Setup(x => x.DeleteOldMessagesAsync(It.IsAny<int>())).ThrowsAsync(new Exception("db down"));
        var service = Create(new RetentionOptions());

        var exception = await Record.ExceptionAsync(service.RunOnceAsync);

        Assert.Null(exception);
    }

    [Fact]
    public async Task StartAsync_RetentionDisabled_ClearsNothing()
    {
        var service = Create(new RetentionOptions { Enabled = false });

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Empty(_cleared);
    }
}
