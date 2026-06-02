namespace NexusProd.Api.Application.Abstractions;

/// <summary>
/// Single seam for "now" — injected everywhere we'd otherwise call
/// <c>DateTimeOffset.UtcNow</c>. Lets unit tests pin time without
/// reaching for static helpers.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
