namespace NexusProd.Api.Domain.Entities;

/// <summary>
/// A user of the system. Maps to the <c>user_master</c> table.
/// </summary>
public sealed class User
{
    public int Id { get; init; }
    public string UserName { get; init; } = string.Empty;
    public int UserBrnchId { get; init; }
    public int UserCounterId { get; init; }
    public bool IsActive { get; init; }

    /// <summary>
    /// Legacy plain-text password column. Used only for the transparent
    /// migration to <see cref="PasswordHash"/>; never read on its own.
    /// </summary>
    public string? LegacyPassword { get; init; }

    /// <summary>
    /// bcrypt hash (or null until the user logs in once and the migration runs).
    /// </summary>
    public string? PasswordHash { get; init; }
}
