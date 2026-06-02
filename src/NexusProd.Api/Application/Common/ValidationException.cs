namespace NexusProd.Api.Application.Common;

/// <summary>
/// Thrown by handlers when input is invalid. Caught by the API layer
/// and translated to HTTP 400 with a stable error payload.
/// </summary>
public sealed class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}
