namespace NexusProd.Api.Application.Abstractions;

/// <summary>
/// Password hasher abstraction. The <c>Base64PasswordHasher</c> is the
/// production implementation; tests can swap in a fake without touching
/// the handlers.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Returns true when <paramref name="plain"/> matches <paramref name="hash"/>.</summary>
    bool Verify(string plain, string hash);

    /// <summary>
    /// Reversibly encodes <paramref name="plain"/> for use as a lookup key
    /// (e.g. the username column stores encoded values). The production
    /// implementation is base64-of-UTF8; the password verification path
    /// uses <see cref="Verify"/> against the bcrypt hash, not this method.
    /// </summary>
    string Encode(string plain);
}
