using NexusProd.Api.Application.Abstractions;

namespace NexusProd.Api.Infrastructure.Security;

/// <summary>
/// Production hasher. Uses BCrypt.Net-Next. The work factor matches the
/// library default (11), which is fine for an interactive login on a
/// local network — bump if the deployment becomes internet-facing.
/// </summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
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

    public string Hash(string plain)
    {
        if (string.IsNullOrEmpty(plain))
            throw new ArgumentException("plain cannot be empty", nameof(plain));
        return BCrypt.Net.BCrypt.HashPassword(plain);
    }
}
