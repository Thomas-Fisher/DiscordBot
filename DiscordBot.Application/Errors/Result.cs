namespace DiscordBot.Application.Errors;

public sealed record Result(Error? Error = null)
{
    public bool Success => Error == null;

    public static Result Ok() => new();

    public static Result Fail(Error error) => new(error);
}

public sealed record Result<T>(T? Value, Error? Error)
{
    public bool Success => Error == null;

    public static Result<T> Ok(T value) => new(value, null);

    public static Result<T> Fail(Error error) => new(default, error);
}
