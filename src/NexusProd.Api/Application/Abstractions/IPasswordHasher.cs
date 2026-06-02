namespace NexusProd.Api.Application.Abstractions;

/// <summary>
/// Password hasher abstraction. The <c>BcryptPasswordHasher</c> is the
/// production implementation; tests can swap in a fake without touching
/// the handlers.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Returns true when <paramref name="plain"/> matches <paramref name="hash"/>.</summary>
    bool Verify(string plain, string hash);

    /// <summary>Generates a new bcrypt hash for <paramref name="plain"/>.</summary>
    string Hash(string plain);
    string Encode(string plain);
}
