using DiscordBot.Domain.Entities;

namespace DiscordBot.Tests.Unit.Domain;

public class RefreshTokenTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Rotate_KeepsTheLoginsEndDate_SoASessionCannotSlideForEver()
    {
        var first = RefreshToken.StartSession(1, "user", null, "hash-1", Now, TimeSpan.FromDays(7), TimeSpan.FromDays(30));

        var second = first.Rotate("hash-2", Now.AddDays(6), TimeSpan.FromDays(7));

        Assert.Equal(first.FamilyId, second.FamilyId);
        Assert.Equal(first.SessionExpiresAt, second.SessionExpiresAt);
        Assert.Equal(Now.AddDays(13), second.ExpiresAt);
    }

    [Fact]
    public void Rotate_NearTheEndOfTheLogin_NeverOutlivesIt()
    {
        var first = RefreshToken.StartSession(1, "user", null, "hash-1", Now, TimeSpan.FromDays(7), TimeSpan.FromDays(30));

        var last = first.Rotate("hash-2", Now.AddDays(28), TimeSpan.FromDays(7));

        Assert.Equal(Now.AddDays(30), last.ExpiresAt);
    }
}
