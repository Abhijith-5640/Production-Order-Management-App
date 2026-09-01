namespace NexusProd.Api.Api.Contracts;

public sealed record SectionDto(int Id, string Name)
{
    public const int NoSectionId = -1;
}
public sealed record SectionsResponse(int CategoryId, IReadOnlyList<SectionDto> Sections);
public sealed record TripsM(int Id, string Trip);
public sealed record TripsResponse(IReadOnlyList<TripsM> Trips);

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

