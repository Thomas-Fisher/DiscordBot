using DiscordBot.Application.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace DiscordBot.Api;

/// <summary>
/// Every refusal the API sends is an RFC 7807 problem: the same shape as the framework's own error responses, with the message in
/// <c>detail</c> and, when there is one, a stable <c>code</c>.
/// </summary>
public static class Problems
{
    public static ProblemDetails Of(int status, string detail, string? code = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),
            Detail = detail
        };

        if (code != null)
        {
            problem.Extensions["code"] = code;
        }

        return problem;
    }

    public static ProblemDetails Of(Error error)
    {
        return Of(error.Kind == ErrorKind.NotFound ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest, error.Message, error.Code);
    }
}
