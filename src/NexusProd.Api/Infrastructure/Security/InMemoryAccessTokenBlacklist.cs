using System.Collections.Concurrent;
using NexusProd.Api.Application.UseCases.Auth;

namespace NexusProd.Api.Infrastructure.Security;

/// <summary>
/// Access-JTI blacklist for explicit logout before expiry. The JWT
/// middleware consults this on every request — same memory-only scope
/// as the refresh store.
/// </summary>
public sealed class InMemoryAccessTokenBlacklist : IAccessTokenBlacklist
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revoked = new();

    public bool IsRevoked(string jti)
        => _revoked.TryGetValue(jti, out var exp) && exp > DateTimeOffset.UtcNow;

    public void Revoke(string jti)
        => _revoked[jti] = DateTimeOffset.UtcNow.AddMinutes(30); // upper bound — access tokens are 15 min
}
