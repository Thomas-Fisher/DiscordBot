using DiscordBot.Api.Middleware;
using DiscordBot.Application.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DiscordBot.Tests.Unit.Api;

public class GlobalExceptionHandlerTests
{
    private static async Task<(bool Handled, ProblemDetails? Problem, int Status)> HandleAsync(string environment, Exception? exception = null, bool requestAborted = false)
    {
        ProblemDetails? captured = null;
        var problemService = new Mock<IProblemDetailsService>();
        problemService.Setup(x => x.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(c => captured = c.ProblemDetails)
            .Returns(ValueTask.FromResult(true));

        var env = new Mock<IHostEnvironment>();
        env.SetupGet(x => x.EnvironmentName).Returns(environment);

        var handler = new GlobalExceptionHandler(problemService.Object, env.Object, NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();

        if (requestAborted)
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            context.RequestAborted = cts.Token;
        }

        var handled = await handler.TryHandleAsync(context, exception ?? new InvalidOperationException("connection string is Server=prod;Password=hunter2"), CancellationToken.None);

        return (handled, captured, context.Response.StatusCode);
    }

    [Fact]
    public async Task TryHandle_UnhandledExceptionInProduction_Returns500WithoutTheExceptionMessage()
    {
        var (handled, problem, status) = await HandleAsync(Environments.Production);

        Assert.True(handled);
        Assert.Equal((500, 500), (status, problem!.Status));
        Assert.Null(problem.Detail);
    }

    [Fact]
    public async Task TryHandle_UnhandledExceptionInDevelopment_IncludesTheExceptionMessageToHelpDebugging()
    {
        var (_, problem, _) = await HandleAsync(Environments.Development);

        Assert.Contains("connection string", problem!.Detail);
    }

    [Fact]
    public async Task TryHandle_ConcurrentUpdateConflict_Returns409SoTheCallerCanReload()
    {
        var (handled, problem, status) = await HandleAsync(Environments.Production, new DbUpdateConcurrencyException("row changed"));

        Assert.True(handled);
        Assert.Equal((409, 409), (status, problem!.Status));
        Assert.Contains("Reload", problem.Detail);
    }

    [Fact]
    public async Task TryHandle_DiscordUnavailable_Returns502BecauseTheRequestWasFine()
    {
        var (handled, problem, status) = await HandleAsync(Environments.Production, new DiscordUnavailableException("down"));

        Assert.True(handled);
        Assert.Equal((502, 502), (status, problem!.Status));
    }

    [Fact]
    public async Task TryHandle_ClientDisconnectedMidRequest_Returns499WithoutAProblemBody()
    {
        var (handled, problem, status) = await HandleAsync(Environments.Production, new OperationCanceledException(), requestAborted: true);

        Assert.True(handled);
        Assert.Equal(499, status);
        Assert.Null(problem);
    }

    [Fact]
    public async Task TryHandle_CancellationThatIsNotTheClientLeaving_IsStillAnError()
    {
        var (_, problem, status) = await HandleAsync(Environments.Production, new OperationCanceledException(), requestAborted: false);

        Assert.Equal(500, status);
        Assert.NotNull(problem);
    }
}
