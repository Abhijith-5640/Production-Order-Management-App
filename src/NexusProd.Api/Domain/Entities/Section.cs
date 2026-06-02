namespace NexusProd.Api.Domain.Entities;

public sealed class Section
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}
