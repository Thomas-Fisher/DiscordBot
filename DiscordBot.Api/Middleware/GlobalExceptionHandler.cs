using DiscordBot.Application.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Api.Middleware;

/// <summary>
/// Turns an unhandled exception into an RFC 7807 problem response. The exception message is only
/// included in Development so internal details never reach API clients.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetailsService, IHostEnvironment environment, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _environment = environment;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // The client went away mid-request: nothing to report and nobody to report it to
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return true;
        }

        // Two requests changed the same row at once and this one lost; the caller can reload and try again
        if (exception is DbUpdateConcurrencyException)
        {
            _logger.LogWarning(exception, "Concurrent update rejected. TraceId: {TraceId}", httpContext.TraceIdentifier);
            return await WriteAsync(httpContext, exception, StatusCodes.Status409Conflict,
                "The data was changed by someone else while you were editing it.", "Reload and try again.");
        }

        // Discord was needed and did not answer: the caller's request was fine, so this is not a 500
        if (exception is DiscordUnavailableException)
        {
            _logger.LogWarning(exception, "Discord unavailable. TraceId: {TraceId}", httpContext.TraceIdentifier);
            return await WriteAsync(httpContext, exception, StatusCodes.Status502BadGateway,
                "Discord could not be reached.", "Try again in a moment.");
        }

        _logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", httpContext.TraceIdentifier);

        return await WriteAsync(httpContext, exception, StatusCodes.Status500InternalServerError,
            "An error occurred processing your request.", _environment.IsDevelopment() ? exception.Message : null);
    }

    private async Task<bool> WriteAsync(HttpContext httpContext, Exception exception, int status, string title, string? detail)
    {
        httpContext.Response.StatusCode = status;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail }
        });
    }
}
