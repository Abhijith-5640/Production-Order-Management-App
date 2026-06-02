namespace NexusProd.Api.Application.Abstractions;

/// <summary>
/// Unit-of-work abstraction. Handlers that need to coordinate multiple
/// SQL operations in a single transaction call <see cref="BeginAsync"/>
/// and the <c>MySqlUnitOfWork</c> implementation wraps a real
/// <c>MySqlTransaction</c>.
/// </summary>
public interface IUnitOfWork
{
    Task<IUnitOfWorkScope> BeginAsync(CancellationToken cancellationToken);
}

public interface IUnitOfWorkScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}
