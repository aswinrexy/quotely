using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Quotely.Api.Security;

/// <summary>
/// Encrypts the credentials a merchant entrusts to us — their Razorpay key secret, their OAuth
/// access and refresh tokens — so that a copy of the database is not a copy of their payment
/// account.
///
/// This is deliberately not ASP.NET Core Data Protection. Data Protection keys rotate and live
/// on the machine's filesystem by default; on a container that is rebuilt on every deploy, that
/// means ciphertext written before a deploy cannot be read after it. The keys here come from
/// configuration, survive redeploys, and can be rotated on purpose rather than by accident.
/// </summary>
public interface ISecretProtector
{
    /// <summary>True when a key is configured. Connections cannot be stored without one.</summary>
    bool IsConfigured { get; }

    /// <summary>Encrypts a secret. The result is safe to store and useless without the key.</summary>
    string Protect(string plaintext);

    /// <summary>
    /// Decrypts a stored secret. Throws <see cref="SecretProtectionException"/> when the
    /// ciphertext was not produced by a key we still hold — which is what a key rotation without
    /// re-encryption looks like, and must never be mistaken for "the merchant has no credentials".
    /// </summary>
    string Unprotect(string ciphertext);
}

public class SecretProtectionException : Exception
{
    public SecretProtectionException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// AES-256-GCM. Authenticated encryption, so a tampered ciphertext fails to decrypt rather than
/// decrypting to something an attacker chose.
///
/// Stored format: v1.{keyId}.{nonce}.{tag}.{ciphertext}, each part base64url. The key id is in
/// the clear on purpose: it is what lets a future key rotation decrypt old rows with the old key
/// while writing new rows with the new one, without a migration that touches every secret at once.
/// </summary>
public class AesGcmSecretProtector : ISecretProtector
{
    private const string Version = "v1";
    private const int NonceBytes = 12;   // 96 bits, the size AES-GCM is specified for
    private const int TagBytes = 16;     // 128 bits, the full tag

    private readonly EncryptionOptions _options;
    private readonly IReadOnlyDictionary<string, byte[]> _keys;
    private readonly string? _primaryKeyId;

    public AesGcmSecretProtector(IOptions<EncryptionOptions> options)
    {
        _options = options.Value;
        _keys = _options.ResolveKeys();
        _primaryKeyId = _options.PrimaryKeyId;
    }

    public bool IsConfigured => _primaryKeyId is not null && _keys.ContainsKey(_primaryKeyId);

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        if (!IsConfigured)
            throw new SecretProtectionException(
                "Credential encryption is not configured (set Encryption__Key).");

        var key = _keys[_primaryKeyId!];
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[bytes.Length];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, bytes, ciphertext, tag);

        return string.Join('.', Version, _primaryKeyId, Base64Url(nonce), Base64Url(tag), Base64Url(ciphertext));
    }

    public string Unprotect(string ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        var parts = ciphertext.Split('.');
        if (parts.Length != 5 || parts[0] != Version)
            throw new SecretProtectionException("The stored credential is not in a format we recognise.");

        if (!_keys.TryGetValue(parts[1], out var key))
            throw new SecretProtectionException(
                $"The stored credential was encrypted with key '{parts[1]}', which is not configured.");

        try
        {
            var nonce = FromBase64Url(parts[2]);
            var tag = FromBase64Url(parts[3]);
            var payload = FromBase64Url(parts[4]);
            var plaintext = new byte[payload.Length];

            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(nonce, payload, tag, plaintext);

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            // Deliberately opaque. Which of the nonce, tag or ciphertext failed is not something
            // a caller needs, and describing it would help someone probing the format.
            throw new SecretProtectionException("The stored credential could not be decrypted.", ex);
        }
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}
