namespace Quotely.Api.Payments;

/// <summary>
/// The seam between the invoice domain and whichever payment provider is configured. Kept
/// deliberately small — an order, two signature checks and a webhook parse — rather than being
/// grown into a generic payments platform for a single provider.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Identifier stored on each payment row, e.g. "Razorpay".</summary>
    string Name { get; }

    /// <summary>The publishable key the browser may hold. Never the secret.</summary>
    string PublicKey { get; }

    /// <summary>True when credentials are configured; endpoints refuse politely when they are not.</summary>
    bool IsConfigured { get; }

    Task<ProviderOrder> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default);

    /// <summary>
    /// Verifies the signature the checkout handed to the browser. Throws
    /// <see cref="PaymentSignatureException"/> when it does not match.
    /// </summary>
    void VerifyCheckoutSignature(CheckoutResult result);

    /// <summary>
    /// Fetches the authoritative state of a payment from the provider. Used after checkout so a
    /// payment is recorded on the provider's word rather than the browser's.
    /// </summary>
    Task<PaymentOutcome> GetPaymentAsync(string providerPaymentId, CancellationToken ct = default);

    /// <summary>
    /// Validates a webhook against the raw request body and parses it. Throws
    /// <see cref="PaymentSignatureException"/> when the signature does not match.
    /// </summary>
    WebhookNotification ParseWebhook(string rawBody, string? signatureHeader, string? eventIdHeader);

    /// <summary>Converts a domain amount to the provider's minor unit without floating point.</summary>
    long ToMinorUnits(decimal amount);
}
