namespace NexusProd.Api.Application.Common;

/// <summary>
/// Stable error codes returned by handlers. Stable so the client can branch on them.
/// </summary>
public enum ErrorCode
{
    Unknown,
    InvalidInput,
    NotFound,
    Unauthorized,
    Forbidden,
    Conflict,
    DatabaseError,
    ConfigurationError
}

public sealed record Error(ErrorCode Code, string Message)
{
    public static readonly Error None = new(ErrorCode.Unknown, string.Empty);

    public static Error InvalidInput(string message) => new(ErrorCode.InvalidInput, message);
    public static Error NotFound(string message) => new(ErrorCode.NotFound, message);
    public static Error Unauthorized(string message) => new(ErrorCode.Unauthorized, message);
    public static Error Forbidden(string message) => new(ErrorCode.Forbidden, message);
    public static Error Conflict(string message) => new(ErrorCode.Conflict, message);
    public static Error DatabaseError(string message) => new(ErrorCode.DatabaseError, message);
    public static Error ConfigurationError(string message) => new(ErrorCode.ConfigurationError, message);
}
