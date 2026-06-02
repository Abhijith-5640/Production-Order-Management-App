namespace NexusProd.Api.Application.Common;

/// <summary>
/// A discriminated result: <see cref="IsSuccess"/> decides between
/// <typeparamref name="T"/> and <see cref="Error"/>. Handlers return this
/// instead of throwing, so the API layer can map cleanly to HTTP responses.
/// </summary>
public readonly record struct Result<T>
{
    public T? Value { get; }
    public Error Error { get; }
    public bool IsSuccess => Error == Error.None;
    public bool IsFailure => !IsSuccess;

    private Result(T value)
    {
        Value = value;
        Error = Error.None;
    }

    private Result(Error error)
    {
        Value = default;
        Error = error;
    }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}
