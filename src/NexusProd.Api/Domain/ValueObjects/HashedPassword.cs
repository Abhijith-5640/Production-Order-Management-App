namespace NexusProd.Api.Domain.ValueObjects;

/// <summary>
/// A bcrypt-hashed password. Wraps the raw string so the hash
/// never accidentally ends up in a log or DTO.
/// </summary>
public readonly record struct HashedPassword
{
    public string Value { get; }

    private HashedPassword(string value) => Value = value;

    public static HashedPassword FromHash(string hash) => new(hash);

    public override string ToString() => "***";
}
