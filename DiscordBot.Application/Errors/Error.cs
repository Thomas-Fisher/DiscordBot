namespace DiscordBot.Application.Errors;

/// <summary>Why something was refused: a stable <c>Code</c> for programs and a <c>Message</c> for people. Catalogues: <c>EventErrors</c>, <c>SignupErrors</c>.</summary>
public sealed record Error(string Code, string Message, ErrorKind Kind = ErrorKind.Invalid);
