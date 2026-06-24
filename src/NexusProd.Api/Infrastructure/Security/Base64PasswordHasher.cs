using NexusProd.Api.Application.Abstractions;

namespace NexusProd.Api.Infrastructure.Security;

/// <summary>
/// Password hasher. Both <see cref="Verify"/> and <see cref="Encode"/>
/// use base64-of-UTF8 — the <c>user_master</c> table stores the encoded
/// form of the plaintext password, so no real hashing is needed for this
/// app's local-network deployment.
/// </summary>
public sealed class Base64PasswordHasher : IPasswordHasher
{
    public bool Verify(string plain, string hash)
    {
        if (string.IsNullOrEmpty(plain) || string.IsNullOrEmpty(hash)) return false;

        string encodedInput = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(plain));

        return encodedInput == hash;
    }
    public string Encode(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return string.Empty;

        string encodedInput = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(plain));

        return encodedInput;
    }
}
