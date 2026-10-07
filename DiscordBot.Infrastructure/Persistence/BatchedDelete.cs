using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Infrastructure.Persistence;

/// <summary>Deletes old rows a few thousand at a time, so locks stay short and normal traffic (and vacuum) can proceed between batches.</summary>
internal static class BatchedDelete
{
    private const int BatchSize = 5000;

    public static async Task<int> OlderThanAsync(DbContext context, string table, string timeColumn, DateTime cutoff, CancellationToken cancellationToken = default)
    {
        var total = 0;
        int deleted;

        do
        {
            // The table and column names are constants of this project (never input), which is why they can be part of the text
#pragma warning disable EF1002
            deleted = await context.Database.ExecuteSqlRawAsync(
                $"DELETE FROM \"{table}\" WHERE ctid IN (SELECT ctid FROM \"{table}\" WHERE \"{timeColumn}\" < {{0}} LIMIT {BatchSize})",
                new object[] { cutoff },
                cancellationToken);
#pragma warning restore EF1002
            total += deleted;
        }
        while (deleted == BatchSize);

        return total;
    }
}
