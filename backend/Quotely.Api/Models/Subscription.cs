namespace Quotely.Api.Models;

/// <summary>
/// A business's subscription to QUOTELY ITSELF — the ₹150/month they pay us.
///
/// This is the other money flow, and it must never be confused with the one in
/// <see cref="MerchantPaymentConnection"/>. That one is a customer paying a business, collected
/// with the business's own credentials. This one is a business paying Quotely, collected with
/// Quotely's. See docs/architecture.md, "Three identities".
///
/// The subscription's state is an explicit <see cref="SubscriptionStatus"/>, never a boolean.
/// "Is this paid?" has more than two answers — a trial has not been paid and should have full
/// access; a past-due account has been paid and should not be locked out over a webhook that is
/// thirty seconds late — and a flag cannot express that.
/// </summary>
public class Subscription
{
    public Guid Id { get; set; }

    /// <summary>One subscription per business, enforced by a unique index.</summary>
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Which plan, by its configured code. "pro" today.</summary>
    public string PlanCode { get; set; } = string.Empty;

    /// <summary>
    /// What this business pays, snapshotted when the subscription was created rather than read
    /// from configuration at display time. A price change must not silently rewrite what somebody
    /// already agreed to.
    /// </summary>
    public decimal Price { get; set; }
    public string Currency { get; set; } = "INR";

    public string Provider { get; set; } = PaymentProviders.Razorpay;

    /// <summary>
    /// Razorpay's subscription id, once one exists. Null through the whole trial: nothing has
    /// been set up at the provider until the business is about to start paying.
    /// </summary>
    public string? ProviderSubscriptionId { get; set; }

    /// <summary>Razorpay's plan id, which we create once and reuse for every subscriber.</summary>
    public string? ProviderPlanId { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trialing;

    /// <summary>Why the subscription is in this state, in words meant for the owner.</summary>
    public string? StatusMessage { get; set; }

    public DateTime? TrialStart { get; set; }

    /// <summary>
    /// When free access ends and billing begins. This is also what is handed to Razorpay as the
    /// subscription's <c>start_at</c>, which is how a trial is expressed there — the first charge
    /// simply happens on that date rather than immediately.
    /// </summary>
    public DateTime? TrialEnd { get; set; }

    /// <summary>The paid period currently in force, as the provider reports it.</summary>
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }

    /// <summary>
    /// When the owner asked to cancel. Kept separately from <see cref="CancelledAt"/> because the
    /// two are different facts: a business that cancels on the 3rd has usually paid to the 28th,
    /// and taking their access away on the 3rd would be taking something they paid for.
    /// </summary>
    public DateTime? CancelRequestedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    /// <summary>When the provider last confirmed a successful charge.</summary>
    public DateTime? LastPaymentAt { get; set; }

    /// <summary>When a charge last failed, which is what moves a subscription to PastDue.</summary>
    public DateTime? LastPaymentFailedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public ICollection<CouponRedemption> Redemptions { get; set; } = new List<CouponRedemption>();

    /// <summary>
    /// Whether free access is still running. Read from the clock rather than from the status,
    /// because a status is only as current as the last thing that wrote it — and a trial ends by
    /// a date passing, which nothing writes.
    /// </summary>
    public bool IsInTrial(DateTime now) =>
        Status == SubscriptionStatus.Trialing && TrialEnd is not null && TrialEnd > now;

    /// <summary>
    /// Whether the business may use the product right now.
    ///
    /// PastDue is deliberately included. A failed charge is a reason to ask someone to update
    /// their card, not a reason to take their invoices away the same hour — and Razorpay retries
    /// on its own for days. Access ends when the subscription is Cancelled or Expired, which are
    /// states something decided, not states a clock drifted into.
    /// </summary>
    public bool GrantsAccess(DateTime now) => Status switch
    {
        SubscriptionStatus.Trialing => TrialEnd is null || TrialEnd > now,
        SubscriptionStatus.Active => true,
        SubscriptionStatus.PastDue => true,
        // A cancelled subscription keeps working until the period they paid for runs out.
        SubscriptionStatus.Cancelled => CurrentPeriodEnd is not null && CurrentPeriodEnd > now,
        _ => false
    };

    /// <summary>The date access ends unless something changes. Null while it is not ending.</summary>
    public DateTime? AccessEndsAt(DateTime now) => Status switch
    {
        SubscriptionStatus.Trialing => TrialEnd,
        SubscriptionStatus.Cancelled => CurrentPeriodEnd,
        _ => null
    };
}

/// <summary>
/// Where a subscription stands. Modelled on what Razorpay actually reports — see
/// docs/razorpay-partner.md for the event list — but expressed in our own words, so a provider
/// change does not ripple into the entitlement rules.
/// </summary>
public enum SubscriptionStatus
{
    /// <summary>Free access, with an end date. Every new business starts here.</summary>
    Trialing = 0,

    /// <summary>Paying, and up to date.</summary>
    Active = 1,

    /// <summary>
    /// A charge failed and the provider is retrying. Still has access: see
    /// <see cref="Subscription.GrantsAccess"/> for why.
    /// </summary>
    PastDue = 2,

    /// <summary>Ended by the owner. Access runs to the end of the period already paid for.</summary>
    Cancelled = 3,

    /// <summary>
    /// Over. A trial that ran out without a subscription, or a paid subscription whose retries
    /// were all exhausted. The only state that actually restricts anything.
    /// </summary>
    Expired = 4
}
