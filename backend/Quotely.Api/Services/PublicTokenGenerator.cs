using System.Security.Cryptography;
using System.Text;

namespace Quotely.Api.Services;

/// <summary>
/// Public quotation links are bearer capabilities: whoever holds the URL may view and respond
/// to that one quotation. The token is therefore 256 bits of cryptographically secure entropy,
/// which makes enumeration infeasible, and only its SHA-256 hash is persisted so a leaked
/// database backup does not hand out working links.
/// </summary>
public static class PublicTokenGenerator
{
    /// <summary>32 random bytes, URL-safe base64 (43 characters, no padding).</summary>
    private const int TokenBytes = 32;

    /// <summary>Length of the hex-encoded SHA-256 hash stored in the database.</summary>
    public const int HashLength = 64;

    public static string CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        return Base64UrlEncode(bytes);
    }

    /// <summary>Lower-case hex SHA-256 of the raw token. Deterministic, so it can index a lookup.</summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>
    /// Rejects anything that cannot be one of our tokens before it reaches the database.
    /// Keeps obviously malformed input (SQL-ish strings, oversized values) out of the lookup path.
    /// </summary>
    public static bool LooksValid(string? token) =>
        !string.IsNullOrWhiteSpace(token) &&
        token.Length is >= 20 and <= 128 &&
        token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
