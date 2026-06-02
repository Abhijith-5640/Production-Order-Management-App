namespace NexusProd.Api.Api.Contracts;

public sealed record DbConfigRequest(
    string Host,
    int Port,
    string User,
    string Password,
    string Database,
    bool? UseMockDb);

public sealed record TestDbRequest(
    string Host,
    int Port,
    string User,
    string Password,
    string Database);

public sealed record SuccessResponse(bool Success = true, string? Message = null);
public sealed record TestDbResponse(bool Success, string Message);

public sealed record CheckUpdateResponse(bool Accepted, string Message);
public sealed record UpdateStatusResponse(
    string Phase,
    string? Message,
    string? LatestVersion,
    DateTimeOffset? LastChecked);
