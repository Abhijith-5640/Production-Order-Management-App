namespace NexusProd.Api.Api.Contracts;

public sealed record SectionsResponse(IReadOnlyList<string> Sections);
public sealed record TripsResponse(IReadOnlyList<string> Trips);

public sealed record HealthResponse(
    string Status,
    string Version,
    DateTimeOffset ServerTime,
    double UptimeSeconds);

public sealed record ServerInfoResponse(
    string Version,
    DateTimeOffset ServerTime,
    double UptimeSeconds,
    IReadOnlyList<string> LanAddresses,
    int Port);
