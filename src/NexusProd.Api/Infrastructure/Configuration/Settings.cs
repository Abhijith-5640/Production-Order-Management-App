namespace NexusProd.Api.Infrastructure.Configuration;

public sealed class JwtSettings
{
    public string Issuer { get; set; } = "NexusProd";
    public string Audience { get; set; } = "NexusProd.Client";
    public string AccessSecret { get; set; } = string.Empty;
    public string RefreshSecret { get; set; } = string.Empty;
    public int AccessTokenLifetimeMinutes { get; set; } = 15;
    public int RefreshTokenLifetimeDays { get; set; } = 7;
    public string CookieName { get; set; } = "rt";
    /// <summary>Seconds a revoked refresh JTI remains valid to absorb concurrent-refresh races.</summary>
    public int RefreshTokenRotationGraceSeconds { get; set; } = 30;
}

public sealed class UpdateServerSettings
{
    public string Url { get; set; } = "https://updates.tradersm.com";
    public int CheckIntervalMinutes { get; set; } = 30;
    public bool Enabled { get; set; } = false;
}
