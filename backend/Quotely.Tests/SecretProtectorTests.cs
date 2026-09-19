using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Quotely.Api.Security;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// Unit tests for the encryption that stands between a leaked database backup and every
/// merchant's payment account. Real AES-GCM throughout — nothing here is stubbed.
/// </summary>
public class SecretProtectorTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static AesGcmSecretProtector Protector(string? key = null, params string[] previous) =>
        new(Options.Create(new EncryptionOptions
        {
            Key = key ?? NewKey(),
            PreviousKeys = previous
        }));

    [Fact]
    public void A_protected_secret_round_trips()
    {
        var protector = Protector();

        protector.Unprotect(protector.Protect("rzp_secret_value")).Should().Be("rzp_secret_value");
    }

    [Fact]
    public void The_ciphertext_does_not_contain_the_plaintext()
    {
        var protector = Protector();

        protector.Protect("the_merchants_key_secret").Should().NotContain("the_merchants_key_secret");
    }

    [Fact]
    public void The_same_secret_encrypts_differently_every_time()
    {
        // A fresh nonce per encryption. Without it, two merchants who happened to use the same
        // secret would produce identical ciphertext, and that equality would itself be a leak.
        var protector = Protector();

        protector.Protect("same").Should().NotBe(protector.Protect("same"));
    }

    [Fact]
    public void A_secret_encrypted_with_another_key_cannot_be_read()
    {
        var cipher = Protector().Protect("secret");

        var act = () => Protector().Unprotect(cipher);

        act.Should().Throw<SecretProtectionException>();
    }

    [Fact]
    public void A_tampered_ciphertext_is_rejected_rather_than_decrypted()
    {
        // This is what authenticated encryption buys. With AES-CBC and no MAC, flipping bits in
        // the ciphertext would flip bits in the plaintext and decryption would happily succeed.
        var protector = Protector();
        var cipher = protector.Protect("secret");

        var parts = cipher.Split('.');
        var payload = parts[4].ToCharArray();
        payload[0] = payload[0] == 'A' ? 'B' : 'A';
        parts[4] = new string(payload);

        var act = () => protector.Unprotect(string.Join('.', parts));

        act.Should().Throw<SecretProtectionException>();
    }

    [Fact]
    public void A_rotated_key_still_reads_secrets_written_with_the_old_one()
    {
        // The property that makes rotation possible without a migration that touches every row
        // at once: each ciphertext names the key that produced it.
        var oldKey = NewKey();
        var cipher = Protector(oldKey).Protect("written_before_the_rotation");

        var rotated = Protector(NewKey(), oldKey);

        rotated.Unprotect(cipher).Should().Be("written_before_the_rotation");
        // …and new writes use the new key, so the old one can eventually be retired.
        rotated.Unprotect(rotated.Protect("written_after")).Should().Be("written_after");
    }

    [Fact]
    public void Dropping_the_old_key_makes_old_secrets_unreadable_loudly()
    {
        // Retiring a key before re-encrypting is a mistake. It must surface as a refusal, never
        // as "this merchant has no credentials" — which would look like an ordinary empty state.
        var oldKey = NewKey();
        var cipher = Protector(oldKey).Protect("secret");

        var act = () => Protector(NewKey()).Unprotect(cipher);

        act.Should().Throw<SecretProtectionException>()
            .WithMessage("*not configured*");
    }

    [Fact]
    public void An_unconfigured_protector_refuses_to_store_anything()
    {
        var protector = Protector(key: "");

        protector.IsConfigured.Should().BeFalse();
        var act = () => protector.Protect("secret");
        act.Should().Throw<SecretProtectionException>();
    }

    [Theory]
    [InlineData("not-base64-at-all!!")]
    [InlineData("c2hvcnQ=")]                                                        // 5 bytes
    [InlineData("QUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQQ==")]                    // 31 bytes
    [InlineData("QUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFB")]                    // 33 bytes
    [InlineData("QUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQQ==")] // 64 bytes
    public void A_key_that_is_not_thirty_two_bytes_is_not_accepted(string key)
    {
        Protector(key).IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void A_malformed_stored_value_is_rejected_rather_than_crashing()
    {
        var protector = Protector();

        foreach (var malformed in new[] { "", "v1", "v1.a.b.c", "v2.a.b.c.d", "garbage" })
        {
            var act = () => protector.Unprotect(malformed);
            act.Should().Throw<SecretProtectionException>();
        }
    }

    [Fact]
    public void A_key_id_reveals_nothing_about_its_key()
    {
        // The id is in the clear in every ciphertext, so it must be a one-way function of the key.
        var key = RandomNumberGenerator.GetBytes(32);
        var id = EncryptionOptions.KeyIdFor(key);

        id.Should().HaveLength(8);
        id.Should().NotContain(Convert.ToBase64String(key)[..4]);
        EncryptionOptions.KeyIdFor(key).Should().Be(id, "the same key always labels its ciphertext the same way");
    }
}
