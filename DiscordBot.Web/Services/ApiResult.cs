using System.Net;
using System.Text.Json;

namespace DiscordBot.Web.Services;

public class ApiResult
{
    protected ApiResult(bool isSuccess, string? errorMessage, HttpStatusCode? status)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Status = status;
    }

    public bool IsSuccess { get; }

    public string? ErrorMessage { get; }

    /// <summary>The HTTP status, or null when the API could not be reached.</summary>
    public HttpStatusCode? Status { get; }

    public static ApiResult Success() => new(true, null, null);

    public static ApiResult Failure(string message, HttpStatusCode? status = null) => new(false, message, status);

    /// <summary>
    /// Turns a failed response into a message. The API answers with <c>{ "error": "..." }</c>, a ProblemDetails object, a bare
    /// JSON string, or nothing at all (a 403 has no body), so each is tried before falling back to a message for the status.
    /// </summary>
    public static string DescribeFailure(HttpStatusCode status, string? body)
    {
        var fromBody = ReadMessage(body);
        if (!string.IsNullOrWhiteSpace(fromBody))
        {
            return fromBody;
        }

        return status switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired. Please log in again.",
            HttpStatusCode.Forbidden => "You don't have permission to do that.",
            HttpStatusCode.NotFound => "That could not be found.",
            HttpStatusCode.TooManyRequests => "Too many requests. Please wait a moment and try again.",
            >= HttpStatusCode.InternalServerError => "The server ran into a problem. Please try again shortly.",
            _ => "The request was not accepted."
        };
    }

    private static string? ReadMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString();
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "error", "detail", "title" })
                {
                    if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    {
                        return value.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON (for example an HTML error page from a proxy): use the status message instead
        }

        return null;
    }
}

public sealed class ApiResult<T> : ApiResult
{
    private ApiResult(T? value, bool isSuccess, string? errorMessage, HttpStatusCode? status)
        : base(isSuccess, errorMessage, status)
    {
        Value = value;
    }

    /// <summary>The response content. Only meaningful when <see cref="ApiResult.IsSuccess"/> is true.</summary>
    public T? Value { get; }

    public static ApiResult<T> Success(T value) => new(value, true, null, null);

    public static new ApiResult<T> Failure(string message, HttpStatusCode? status = null) => new(default, false, message, status);
}
