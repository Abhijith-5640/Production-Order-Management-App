namespace NexusProd.Api.Application.Common;

/// <summary>
/// Marker for a use-case handler. Keeps DI registration simple
/// and makes the contract obvious in the unit tests.
/// </summary>
public interface IHandler<in TRequest, TResponse>
{
    Task<Result<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken);
}
