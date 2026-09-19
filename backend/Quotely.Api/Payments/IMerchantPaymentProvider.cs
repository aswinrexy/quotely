namespace Quotely.Api.Payments;

/// <summary>
/// The seam between the invoice domain and whichever payment provider collects for a merchant.
///
/// This replaces V2.6's <c>IPaymentProvider</c>, which held one account's credentials in its own
/// fields and therefore could only ever collect into one account. Every method now takes a
/// <see cref="MerchantPaymentContext"/>, so the merchant is part of the call rather than part of
/// the object — and a Razorpay account belonging to Business A cannot be used for Business B's
/// invoice, because the credentials arrive with the invoice's own tenant attached.
///
/// This interface is for MERCHANT money — a customer paying a business that uses Quotely. Money
/// paid TO Quotely for the SaaS subscription goes through <c>ISaasBillingProvider</c> instead,
/// and the two are never wired to the same credentials.
/// </summary>
public interface IMerchantPaymentProvider
{
    /// <summary>Identifier stored on each payment row, e.g. "Razorpay".</summary>
    string Name { get; }

    Task<ProviderOrder> CreateOrderAsync(
        MerchantPaymentContext context, CreateOrderRequest request, CancellationToken ct = default);

    /// <summary>
    /// Verifies the signature the checkout handed to the browser, where the merchant's signing
    /// secret is something we hold. Under OAuth it is not, so this reports that it could not
    /// check rather than pretending it did — see <see cref="CheckoutVerification"/>.
    /// </summary>
    CheckoutVerification VerifyCheckoutSignature(MerchantPaymentContext context, CheckoutResult result);

    /// <summary>
    /// Fetches the authoritative state of a payment from the provider, using the merchant's own
    /// credentials. Used after checkout so a payment is recorded on the provider's word rather
    /// than the browser's.
    /// </summary>
    Task<PaymentOutcome> GetPaymentAsync(
        MerchantPaymentContext context, string providerPaymentId, CancellationToken ct = default);

    /// <summary>
    /// Validates a webhook against the raw body using the secret belonging to the connection the
    /// delivery was addressed to, and parses it. Throws <see cref="PaymentSignatureException"/>
    /// when the signature does not match.
    ///
    /// The secret is passed in rather than read from a field because there is one per merchant:
    /// a signature that verifies for Business A must not verify for Business B.
    /// </summary>
    WebhookNotification ParseWebhook(
        string webhookSecret, string rawBody, string? signatureHeader, string? eventIdHeader);

    /// <summary>
    /// Proves a set of credentials actually works, by making a real call with them. Used when a
    /// merchant connects, so a typo is reported at the moment they press the button rather than
    /// to their first paying customer.
    /// </summary>
    Task<MerchantAccountProbe> ProbeAsync(MerchantPaymentContext context, CancellationToken ct = default);

    /// <summary>Converts a domain amount to the provider's minor unit without floating point.</summary>
    long ToMinorUnits(decimal amount);
}

/// <summary>
/// The result of checking a checkout signature.
///
/// A boolean would be wrong here. "Verified" and "could not be verified locally, ask the provider"
/// are different facts, and collapsing them would mean an OAuth connection either rejects every
/// legitimate payment or accepts an unverified one. Both are unacceptable, so the distinction is
/// kept in the type and the caller is forced to look at it.
/// </summary>
public enum CheckoutVerification
{
    /// <summary>The signature matched. The provider produced this result.</summary>
    Verified = 0,

    /// <summary>
    /// We hold no signing secret for this merchant, so the browser's claim is neither confirmed
    /// nor denied. The caller must confirm the payment with the provider before recording money.
    /// </summary>
    NotVerifiable = 1
}

/// <summary>
/// What a successful credential check tells us about the account behind it. Safe to show an
/// owner: an account identifier and a display name, never a secret.
/// </summary>
public sealed record MerchantAccountProbe(string? ProviderAccountId, string? DisplayName);
