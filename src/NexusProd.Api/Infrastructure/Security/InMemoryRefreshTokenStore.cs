using System.Collections.Concurrent;
using NexusProd.Api.Application.Abstractions;

namespace NexusProd.Api.Infrastructure.Security;

/// <summary>
/// In-memory JTI store. A periodic sweep drops expired and superseded JTIs so the
/// dictionary doesn't grow unbounded. POC scope — swap with Redis for
/// multi-instance deployments.
/// </summary>
public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly ConcurrentDictionary<string, Entry> _store = new();

    public Task StoreAsync(string jti, int userId, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        _store[jti] = new Entry(userId, expiresAt, RefreshTokenStatus.Active, null, null);
        return Task.CompletedTask;
    }

    public Task<int?> GetUserIdAsync(string jti, CancellationToken cancellationToken)
    {
        if (!_store.TryGetValue(jti, out var entry)) return Task.FromResult<int?>(null);
        if (entry.Status != RefreshTokenStatus.Active) return Task.FromResult<int?>(null);
        if (entry.ExpiresAt <= DateTimeOffset.UtcNow) return Task.FromResult<int?>(null);
        return Task.FromResult<int?>(entry.UserId);
    }

    public Task RevokeAsync(string jti, bool isSuperseded, CancellationToken cancellationToken)
    {
        if (_store.TryGetValue(jti, out var entry))
        {
            var newStatus = isSuperseded ? RefreshTokenStatus.Superseded : RefreshTokenStatus.HardRevoked;
            _store[jti] = entry with
            {
                Status = newStatus,
                SupersededAt = isSuperseded ? DateTimeOffset.UtcNow : entry.SupersededAt
            };
        }
        return Task.CompletedTask;
    }

    public Task<RefreshTokenStatus?> GetTokenStatusAsync(string jti, CancellationToken cancellationToken)
    {
        if (!_store.TryGetValue(jti, out var entry)) return Task.FromResult<RefreshTokenStatus?>(null);
        if (entry.ExpiresAt <= DateTimeOffset.UtcNow) return Task.FromResult<RefreshTokenStatus?>(null);
        return Task.FromResult<RefreshTokenStatus?>(entry.Status);
    }

    public Task<bool> WasSupersededWithinGraceAsync(string jti, TimeSpan gracePeriod, CancellationToken cancellationToken)
    {
        if (!_store.TryGetValue(jti, out var entry)) return Task.FromResult(false);
        if (entry.Status != RefreshTokenStatus.Superseded) return Task.FromResult(false);
        if (!entry.SupersededAt.HasValue) return Task.FromResult(false);
        if (DateTimeOffset.UtcNow - entry.SupersededAt.Value > gracePeriod) return Task.FromResult(false);
        return Task.FromResult(true);
    }

    public Task CleanupExpiredAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _store)
        {
            if (kvp.Value.ExpiresAt <= now)
            {
                _store.TryRemove(kvp.Key, out _);
            }
            else if (kvp.Value.Status == RefreshTokenStatus.Superseded
                     && kvp.Value.SupersededAt.HasValue
                     && now - kvp.Value.SupersededAt.Value > TimeSpan.FromMinutes(5))
            {
                // Superseded entries past their grace window can go too.
                _store.TryRemove(kvp.Key, out _);
            }
        }
        return Task.CompletedTask;
    }

    private sealed record Entry(
        int UserId,
        DateTimeOffset ExpiresAt,
        RefreshTokenStatus Status,
        string? ReplacedByJti,
        DateTimeOffset? SupersededAt);
}
