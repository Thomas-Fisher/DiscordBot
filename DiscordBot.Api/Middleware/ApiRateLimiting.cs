using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using DiscordBot.Application;

namespace DiscordBot.Api.Middleware;

/// <summary>
/// Fixed-window limits (the built-in ASP.NET Core limiter) per caller (the signed-in user, else the IP address) and endpoint
/// group. The group is the matching <see cref="RateLimitingOptions.EndpointLimits"/> entry, never the raw path, so varying an
/// id in the URL earns no fresh allowance.
/// </summary>
public static class ApiRateLimiting
{
    private const string DefaultGroup = "default";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitingOptions>(configuration.GetSection("RateLimiting"));

        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitingOptions>>((limiter, settings) =>
        {
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => PartitionFor(context, settings.Value));
            limiter.OnRejected = WriteRejectionAsync;
        });

        services.AddRateLimiter(_ => { });
        return services;
    }

    public static RateLimitPartition<string> PartitionFor(HttpContext context, RateLimitingOptions options)
    {
        if (!options.Enabled)
        {
            return RateLimitPartition.GetNoLimiter("disabled");
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (options.BypassIPs.Contains(ip))
        {
            return RateLimitPartition.GetNoLimiter("bypass");
        }

        var (group, limit) = LimitFor(context.Request.Path, options);
        var caller = context.User.FindFirst(AuthClaims.UserId)?.Value is { } userId ? $"user:{userId}" : $"ip:{ip}";

        return RateLimitPartition.GetFixedWindowLimiter($"{caller}|{group}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromMinutes(Math.Max(options.WindowMinutes, 1)),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    private static (string Group, int Limit) LimitFor(PathString path, RateLimitingOptions options)
    {
        foreach (var (pattern, limit) in options.EndpointLimits)
        {
            if (Matches(path.Value ?? "", pattern))
            {
                return (pattern, limit);
            }
        }

        return (DefaultGroup, options.MaxRequestsPerWindow);
    }

    private static bool Matches(string path, string pattern)
    {
        if (pattern is "" or "*")
        {
            return true;
        }

        return pattern.EndsWith('*')
            ? path.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        await response.WriteAsJsonAsync(new ProblemDetails
        {
            Title = "Too many requests",
            Status = StatusCodes.Status429TooManyRequests,
            Detail = "Rate limit exceeded. Wait a moment and try again.",
            Instance = context.HttpContext.Request.Path
        }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", cancellationToken);
    }
}
