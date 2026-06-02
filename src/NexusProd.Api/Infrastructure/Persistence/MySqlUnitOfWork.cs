using MySqlConnector;
using NexusProd.Api.Application.Abstractions;

namespace NexusProd.Api.Infrastructure.Persistence;

/// <summary>
/// Opens a connection, begins a transaction, and exposes both via
/// <see cref="Connection"/> / <see cref="Transaction"/>. Repositories
/// that want to participate in the same transaction receive the scope
/// and read those properties.
/// </summary>
public sealed class MySqlUnitOfWork : IUnitOfWork
{
    private readonly MySqlConnectionFactory _factory;

    public MySqlUnitOfWork(MySqlConnectionFactory factory) => _factory = factory;

    public async Task<IUnitOfWorkScope> BeginAsync(CancellationToken cancellationToken)
    {
        var conn = await _factory.OpenAsync(cancellationToken);
        var tx = await conn.BeginTransactionAsync(cancellationToken);
        return new Scope(conn, tx);
    }

    private sealed class Scope : IUnitOfWorkScope
    {
        public MySqlConnection Connection { get; }
        public MySqlTransaction Transaction { get; private set; } = null!;
        private bool _completed;

        public Scope(MySqlConnection connection, MySqlTransaction transaction)
        {
            Connection = connection;
            Transaction = transaction;
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await Transaction.CommitAsync(cancellationToken);
            _completed = true;
        }

        public async Task RollbackAsync(CancellationToken cancellationToken)
        {
            await Transaction.RollbackAsync(cancellationToken);
            _completed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_completed)
            {
                try { await Transaction.RollbackAsync(); } catch { /* ignore */ }
            }
            await Transaction.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
