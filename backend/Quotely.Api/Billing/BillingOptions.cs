namespace Quotely.Api.Billing;

/// <summary>
/// What Quotely charges, how long the free period is, and what an expired subscription may still
/// do. All configuration, because every one of these is a commercial decision that should not
/// need a deployment to change — and because the gating rules in particular have to be adjustable
/// while the product is young.
/// </summary>
public class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>
    /// Switches subscription billing on. Off by default, so a deployment that has not been set up
    /// for it behaves exactly as it did before rather than charging anyone by surprise.
    ///
    /// While this is false every business has full access and no subscription is ever created at
    /// the provider. Nothing is silently accruing in the background.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether an expired subscription actually restricts anything. Separate from
    /// <see cref="Enabled"/> so billing can be introduced — plans visible, subscriptions real —
    /// before the day anybody is locked out of creating an invoice.
    /// </summary>
    public bool EnforceEntitlements { get; set; }

    /// <summary>
    /// Free days for a business that signs up without a coupon. Quotely's launch offer is the
    /// six-month coupon; this is the ordinary trial for everyone else.
    /// </summary>
    public int DefaultTrialDays { get; set; } = 14;

    /// <summary>
    /// How long past a failed payment a subscription keeps working before it expires. Razorpay
    /// retries on its own for several days, so expiring sooner than it gives up would lock out
    /// businesses whose payment was about to succeed.
    /// </summary>
    public int GraceDays { get; set; } = 14;

    /// <summary>The plan a new subscription is created on.</summary>
    public string DefaultPlanCode { get; set; } = "pro";

    public List<BillingPlanOptions> Plans { get; set; } = new()
    {
        new BillingPlanOptions
        {
            Code = "pro",
            Name = "Quotely Pro",
            Description = "Everything in Quotely, for one business.",
            Price = 150m,
            Currency = "INR",
            Interval = "monthly"
        }
    };

    /// <summary>
    /// What an account WITHOUT access may still do. Deliberately generous, and deliberately
    /// configurable: someone who stops paying does not stop owning their own records, and a
    /// product that holds a small business's invoice history hostage is not one worth building.
    /// </summary>
    public EntitlementOptions WithoutSubscription { get; set; } = new();

    public BillingPlanOptions? FindPlan(string code) =>
        Plans.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));

    public BillingPlanOptions DefaultPlan =>
        FindPlan(DefaultPlanCode)
        ?? Plans.FirstOrDefault()
        ?? throw new InvalidOperationException("No billing plan is configured.");
}

public class BillingPlanOptions
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "INR";

    /// <summary>Razorpay's own vocabulary: daily, weekly, monthly or yearly.</summary>
    public string Interval { get; set; } = "monthly";

    public int IntervalCount { get; set; } = 1;
}

/// <summary>
/// One switch per thing a business might be stopped from doing. Read by
/// <c>ISubscriptionEntitlementService</c> and nowhere else.
/// </summary>
public class EntitlementOptions
{
    /// <summary>Signing in is never withheld. Locking someone out of their own account is not a
    /// dunning strategy, and they cannot pay us from a login page they cannot pass.</summary>
    public bool CanSignIn { get; set; } = true;

    /// <summary>Reading what they already have. Their records, not ours.</summary>
    public bool CanViewExistingData { get; set; } = true;

    /// <summary>Getting their data out. Withholding this would be holding it hostage.</summary>
    public bool CanExportData { get; set; } = true;

    /// <summary>Reaching the billing page, which is how they would fix this.</summary>
    public bool CanManageBilling { get; set; } = true;

    // ---- what actually stops ----

    public bool CanCreateQuotation { get; set; }
    public bool CanCreateInvoice { get; set; }

    /// <summary>
    /// Whether invoices already issued can still be paid. True on purpose: a customer settling an
    /// invoice they were sent last week is not the person whose subscription lapsed, and that
    /// money belongs to the business regardless of what they owe us.
    /// </summary>
    public bool CanAcceptPayments { get; set; } = true;
}
