namespace NexusProd.Api.Api.Contracts;

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset AccessExpiresAt,
    string User,
    int UserId);

public sealed record RefreshRequest(string RefreshToken);

public sealed record RefreshResponse(string AccessToken, DateTimeOffset AccessExpiresAt);

public sealed record MeResponse(int UserId, string UserName);

public sealed record LogoutResponse(bool Success = true);

public sealed record ErrorResponse(string Message, string? Code = null);
