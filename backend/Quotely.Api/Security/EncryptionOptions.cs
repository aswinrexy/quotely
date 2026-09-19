using System.Security.Cryptography;

namespace Quotely.Api.Security;

/// <summary>
/// The key material protecting merchant credentials at rest. Supplied as configuration only —
/// Encryption__Key in the host's secret store — and never written to this repository.
///
/// Rotation is supported without downtime: move the current value into Encryption__PreviousKeys__0
/// and put the new one in Encryption__Key. Rows written before the rotation still decrypt with the
/// old key, because each ciphertext records which key produced it.
/// </summary>
public class EncryptionOptions
{
    public const string SectionName = "Encryption";

    /// <summary>
    /// The active key: 32 bytes, base64 encoded. Generate one with
    /// <c>openssl rand -base64 32</c> and store it in the host's secret store, nowhere else.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Keys that are no longer used for writing but must still decrypt existing rows. Empty
    /// until the first rotation.
    /// </summary>
    public string[] PreviousKeys { get; set; } = Array.Empty<string>();

    /// <summary>
    /// A short, stable identifier for a key, derived from the key itself rather than configured
    /// separately — so the two can never drift apart and label a ciphertext with the wrong key.
    /// It is the first eight hex characters of the key's SHA-256, which reveals nothing about the
    /// key: reversing it would mean reversing SHA-256.
    /// </summary>
    public static string KeyIdFor(byte[] key) =>
        Convert.ToHexString(SHA256.HashData(key))[..8].ToLowerInvariant();

    public string? PrimaryKeyId => TryDecode(Key, out var key) ? KeyIdFor(key) : null;

    /// <summary>
    /// Every key we can decrypt with, indexed by id. Malformed entries are dropped rather than
    /// throwing: one bad value in the previous-keys list must not take the application down.
    /// </summary>
    public IReadOnlyDictionary<string, byte[]> ResolveKeys()
    {
        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var candidate in new[] { Key }.Concat(PreviousKeys))
        {
            if (!TryDecode(candidate, out var key)) continue;
            keys[KeyIdFor(key)] = key;
        }

        return keys;
    }

    /// <summary>AES-256 needs exactly 32 bytes. Anything else is a configuration mistake.</summary>
    private static bool TryDecode(string value, out byte[] key)
    {
        key = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value)) return false;

        Span<byte> buffer = stackalloc byte[64];
        if (!Convert.TryFromBase64String(value.Trim(), buffer, out var written) || written != 32)
            return false;

        key = buffer[..written].ToArray();
        return true;
    }
}
