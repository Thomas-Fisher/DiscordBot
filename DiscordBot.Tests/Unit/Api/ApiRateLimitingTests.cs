using System.Security.Claims;
using System.Threading.RateLimiting;
using DiscordBot.Api.Middleware;
using Microsoft.AspNetCore.Http;

namespace DiscordBot.Tests.Unit.Api;

public class ApiRateLimitingTests
{
    private static PartitionedRateLimiter<HttpContext> LimiterFor(RateLimitingOptions options)
    {
        return PartitionedRateLimiter.Create<HttpContext, string>(context => ApiRateLimiting.PartitionFor(context, options));
    }

    private static DefaultHttpContext NewContext(string path = "/api/events/1", string ip = "10.0.0.1", string? userId = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);

        if (userId != null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("discord_user_id", userId) }, "test"));
        }

        return context;
    }

    private static bool Allowed(PartitionedRateLimiter<HttpContext> limiter, HttpContext context)
    {
        using var lease = limiter.AttemptAcquire(context);
        return lease.IsAcquired;
    }

    [Fact]
    public void Acquire_MoreRequestsThanTheLimitFromOneCaller_IsRefused()
    {
        using var limiter = LimiterFor(new RateLimitingOptions { MaxRequestsPerWindow = 2 });

        var results = Enumerable.Range(0, 3).Select(_ => Allowed(limiter, NewContext())).ToList();

        Assert.Equal(new[] { true, true, false }, results);
    }

    [Fact]
    public void Acquire_DifferentUrlsInTheSameEndpointGroup_ShareOneAllowance()
    {
        // /api/events/1, /api/events/2 ... must not each get a fresh bucket
        var options = new RateLimitingOptions { MaxRequestsPerWindow = 100 };
        options.EndpointLimits["/api/events/*"] = 3;
        using var limiter = LimiterFor(options);

        var results = Enumerable.Range(1, 6).Select(i => Allowed(limiter, NewContext($"/api/events/{i}"))).ToList();

        Assert.Equal(3, results.Count(allowed => allowed));
    }

    [Fact]
    public void Acquire_DifferentCallers_EachHaveTheirOwnAllowance()
    {
        using var limiter = LimiterFor(new RateLimitingOptions { MaxRequestsPerWindow = 1 });

        var results = new[]
        {
            Allowed(limiter, NewContext(ip: "10.0.0.1")),
            Allowed(limiter, NewContext(ip: "10.0.0.2")),
            Allowed(limiter, NewContext(ip: "10.0.0.3", userId: "42")),
            Allowed(limiter, NewContext(ip: "10.0.0.4", userId: "43"))
        };

        Assert.All(results, Assert.True);
    }

    [Fact]
    public void Acquire_SignedInUserOnSeveralAddresses_IsCountedAsOneCaller()
    {
        using var limiter = LimiterFor(new RateLimitingOptions { MaxRequestsPerWindow = 1 });

        var first = Allowed(limiter, NewContext(ip: "10.0.0.1", userId: "42"));
        var second = Allowed(limiter, NewContext(ip: "10.0.0.2", userId: "42"));

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public void Acquire_EndpointGroupWithItsOwnLimit_IsRefusedAtThatLimitUnlessTheAddressIsOnTheBypassList()
    {
        var options = new RateLimitingOptions { MaxRequestsPerWindow = 100, BypassIPs = { "10.0.0.9" } };
        options.EndpointLimits["/api/auth/*"] = 1;
        using var limiter = LimiterFor(options);

        var results = new[]
        {
            Allowed(limiter, NewContext("/api/auth/refresh")),
            Allowed(limiter, NewContext("/api/auth/refresh")),
            Allowed(limiter, NewContext("/api/auth/refresh", "10.0.0.9")),
            Allowed(limiter, NewContext("/api/auth/refresh", "10.0.0.9"))
        };

        Assert.Equal(new[] { true, false, true, true }, results);
    }

    [Fact]
    public void Acquire_LimitingSwitchedOff_NeverRefuses()
    {
        using var limiter = LimiterFor(new RateLimitingOptions { Enabled = false, MaxRequestsPerWindow = 1 });

        var results = Enumerable.Range(0, 5).Select(_ => Allowed(limiter, NewContext())).ToList();

        Assert.All(results, Assert.True);
    }
}
