using Quotely.Api.Models;

namespace Quotely.Api.Payments;

/// <summary>
/// Which merchant a payment operation acts on behalf of, and the credentials to do it with.
///
/// Every call into <see cref="IMerchantPaymentProvider"/> takes one of these. That is the point:
/// there is no overload that omits it, so "collect this payment" cannot be expressed without
/// first having said whose account collects it. A missing merchant is a compile error rather
/// than a silent fallback to somebody else's Razorpay.
///
/// These are constructed in exactly one place — <c>MerchantConnectionService.ResolveAsync</c> —
/// which decrypts the stored credentials after checking the connection belongs to the tenant that
/// owns the invoice. Nothing else may build one.
/// </summary>
public sealed record MerchantPaymentContext
{
    /// <summary>The connection row this came from, recorded on the payment for traceability.</summary>
    public required Guid ConnectionId { get; init; }

    /// <summary>
    /// The tenant. Carried through so the outcome of a provider call can be checked against the
    /// invoice it is about to be applied to, rather than assumed to match.
    /// </summary>
    public required Guid UserId { get; init; }

    public required string Provider { get; init; }

    public required PaymentEnvironment Environment { get; init; }

    public required MerchantCredentials Credentials { get; init; }

    /// <summary>
    /// The merchant's account at the provider, where the provider gives us one. Null for a
    /// key-pair connection: the key itself already identifies the account.
    /// </summary>
    public string? ProviderAccountId => Credentials.ProviderAccountId;
}

/// <summary>
/// Decrypted, in memory, for the length of one operation. Never logged, never serialised, never
/// returned from a controller. The record exists so the plaintext has a short, obvious lifetime
/// instead of being passed around as loose strings.
/// </summary>
public sealed record MerchantCredentials
{
    public required MerchantConnectionMode Mode { get; init; }

    /// <summary>Publishable. This is the only field the browser is ever given.</summary>
    public required string PublicKey { get; init; }

    /// <summary>The merchant's Razorpay key secret. Key-pair connections only.</summary>
    public string? KeySecret { get; init; }

    /// <summary>The scoped OAuth access token. OAuth connections only.</summary>
    public string? AccessToken { get; init; }

    public string? ProviderAccountId { get; init; }

    /// <summary>
    /// What a checkout signature is verified with. Razorpay signs the checkout response with the
    /// key secret in key-pair mode; under OAuth the merchant's secret is precisely what we do not
    /// have, so there is nothing to verify against locally and the provider must be asked instead.
    /// Null therefore means "cannot verify locally", not "verification passes".
    /// </summary>
    public string? CheckoutSigningSecret => Mode == MerchantConnectionMode.KeyPair ? KeySecret : null;

    /// <summary>
    /// Deliberately overridden so a credentials object cannot reach a log through an interpolated
    /// string. The compiler-generated version would print every property, secrets included.
    /// </summary>
    public override string ToString() => $"MerchantCredentials {{ Mode = {Mode}, PublicKey = {Redact(PublicKey)} }}";

    private static string Redact(string value) =>
        string.IsNullOrEmpty(value) ? "(none)" : value.Length <= 8 ? "***" : value[..8] + "***";
}
