/// <summary>A change to an existing signup. Only the values that are set are applied.</summary>
public record SignupUpdate(string? Status = null, string? WowClass = null, string? WowSpec = null);
