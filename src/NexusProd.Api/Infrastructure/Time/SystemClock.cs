using NexusProd.Api.Application.Abstractions;

namespace NexusProd.Api.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
