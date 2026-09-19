using Quotely.Api.Models;

namespace Quotely.Api.Billing;

/// <summary>
/// How Quotely collects ITS OWN ₹150/month from the businesses using it.
///
/// Kept entirely apart from <c>IMerchantPaymentProvider</c>, which is how a business collects
/// from ITS customers. The two are different money, different accounts and different bank
/// destinations, and the separation is the most important architectural rule in this codebase.
/// Nothing implements both interfaces, nothing shares credentials between them, and no service
/// depends on both without saying in its own comments why.
///
/// Every method here acts on QUOTELY'S Razorpay account, using the credentials in the Razorpay
/// configuration section. There is no merchant context, because there is no merchant — the
/// merchant in this flow is us.
/// </summary>
public interface ISaasBillingProvider
{
    string Name { get; }

    /// <summary>True when Quotely's own Razorpay credentials are configured.</summary>
    bool IsConfigured { get; }

    /// <summary>The publishable key the billing page hands to Razorpay's checkout.</summary>
    string PublicKey { get; }

    /// <summary>
    /// Finds or creates the Razorpay plan matching our configured one, returning its id. Called
    /// once per plan and then remembered, because a plan is a provider-side object we own rather
    /// than something per subscriber.
    /// </summary>
    Task<string> EnsurePlanAsync(BillingPlanOptions plan, CancellationToken ct = default);

    /// <summary>
    /// Creates a subscription at the provider.
    ///
    /// <paramref name="startAt"/> is how a free period is expressed: Razorpay begins charging on
    /// that date, so a subscription created today with a start date six months out is six months
    /// free followed by ₹150/month. That is a documented Razorpay behaviour, not a trick — see
    /// docs/razorpay-partner.md.
    /// </summary>
    Task<ProviderSubscription> CreateSubscriptionAsync(
        string providerPlanId,
        int? totalCycles,
        DateTime? startAt,
        IReadOnlyDictionary<string, string> notes,
        CancellationToken ct = default);

    /// <summary>Reads the authoritative state of a subscription from the provider.</summary>
    Task<ProviderSubscription> GetSubscriptionAsync(string providerSubscriptionId, CancellationToken ct = default);

    /// <summary>
    /// Cancels at the provider. <paramref name="atCycleEnd"/> true leaves the business with the
    /// period they have already paid for, which is the only defensible default.
    /// </summary>
    Task<ProviderSubscription> CancelSubscriptionAsync(
        string providerSubscriptionId, bool atCycleEnd, CancellationToken ct = default);

    /// <summary>
    /// Verifies a billing webhook against the raw body using QUOTELY'S webhook secret, and parses
    /// it. A merchant's webhook secret must never verify here, and this secret must never verify
    /// a merchant's delivery — which is why the two paths do not share a method.
    /// </summary>
    SubscriptionNotification ParseWebhook(string rawBody, string? signatureHeader, string? eventIdHeader);

    /// <summary>
    /// Verifies the signature Razorpay's checkout returns after the business authorises the
    /// mandate. Signed over "{subscription_id}|{payment_id}" — a different composition from the
    /// order-based one used for invoice payments, which is why it is a separate method rather
    /// than a shared helper that takes two strings.
    /// </summary>
    void VerifySubscriptionSignature(string providerSubscriptionId, string providerPaymentId, string signature);
}

/// <summary>A subscription as the provider describes it, in our vocabulary.</summary>
public sealed record ProviderSubscription
{
    public required string Id { get; init; }
    public required SubscriptionStatus Status { get; init; }
    /// <summary>The provider's own status string, kept for logging and for the admin view.</summary>
    public required string ProviderStatus { get; init; }
    public DateTime? CurrentStart { get; init; }
    public DateTime? CurrentEnd { get; init; }
    public DateTime? ChargeAt { get; init; }
    public DateTime? EndedAt { get; init; }
    /// <summary>Where to send the business to authorise the mandate, when the provider gives one.</summary>
    public string? ShortUrl { get; init; }
}

/// <summary>A verified billing webhook, reduced to what the subscription domain acts on.</summary>
public sealed record SubscriptionNotification
{
    public required string EventId { get; init; }
    public required string EventType { get; init; }

    /// <summary>Null for events we recognise but take no action on.</summary>
    public ProviderSubscription? Subscription { get; init; }

    /// <summary>True when this event carried a successful charge.</summary>
    public bool Charged { get; init; }

    /// <summary>True when this event reported a failed charge.</summary>
    public bool PaymentFailed { get; init; }
}
