using DiscordBot.Domain.Common;

namespace DiscordBot.Tests.Unit.Domain;

public class UniqueIdTests
{
    [Fact]
    public void Next_CalledRepeatedly_ReturnsStrictlyIncreasingValues()
    {
        var values = new List<ulong>();

        for (var i = 0; i < 10_000; i++)
        {
            values.Add(UniqueId.Next());
        }

        Assert.Equal(values.OrderBy(v => v), values);
        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Fact]
    public async Task Next_CalledFromManyThreads_NeverReturnsDuplicates()
    {
        var workers = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => Enumerable.Range(0, 5_000).Select(__ => UniqueId.Next()).ToArray()))
            .ToArray();

        var all = (await Task.WhenAll(workers)).SelectMany(ids => ids).ToList();

        Assert.Equal(all.Count, all.Distinct().Count());
    }
}
