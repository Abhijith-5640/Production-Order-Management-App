using System.Collections.Concurrent;
using NexusProd.Api.Application.Abstractions;

namespace NexusProd.Api.Infrastructure.Security;

/// <summary>
/// In-memory JTI store. A periodic sweep drops expired JTIs so the
/// dictionary doesn't grow unbounded. POC scope — swap with Redis for
/// multi-instance deployments.
/// </summary>
public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly ConcurrentDictionary<string, Entry> _store = new();

    public Task StoreAsync(string jti, int userId, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        _store[jti] = new Entry(userId, expiresAt, false);
        return Task.CompletedTask;
    }

    public Task<int?> GetUserIdAsync(string jti, CancellationToken cancellationToken)
    {
        if (!_store.TryGetValue(jti, out var entry)) return Task.FromResult<int?>(null);
        if (entry.Revoked) return Task.FromResult<int?>(null);
        if (entry.ExpiresAt <= DateTimeOffset.UtcNow) return Task.FromResult<int?>(null);
        return Task.FromResult<int?>(entry.UserId);
    }

    public Task RevokeAsync(string jti, CancellationToken cancellationToken)
    {
        if (_store.TryGetValue(jti, out var entry))
        {
            _store[jti] = entry with { Revoked = true };
        }
        return Task.CompletedTask;
    }

    private sealed record Entry(int UserId, DateTimeOffset ExpiresAt, bool Revoked);
}
